using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bagly.Api.Options;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Services;

public interface IRazorpayService
{
    bool IsConfigured { get; }
    string KeyId { get; }
    string Currency { get; }
    long ToPaise(decimal amountInr);
    Task<RazorpayOrderResult> CreateOrderAsync(decimal amountInr, string receipt, CancellationToken cancellationToken = default);
    bool VerifyPaymentSignature(string razorpayOrderId, string razorpayPaymentId, string razorpaySignature);

    /// <summary>
    /// Best-effort full refund for a captured payment (used when stock ran out after payment
    /// succeeded). Returns false — never throws — if Razorpay is not configured, the payment id
    /// is missing, or the refund call fails, so callers can fall back to a manual-support path.
    /// </summary>
    Task<bool> TryRefundPaymentAsync(string razorpayPaymentId, string reason, CancellationToken cancellationToken = default);
}

public record RazorpayOrderResult(string Id, long Amount, string Currency, string Receipt, string Status, string RawJson);

public class RazorpayService(
    HttpClient httpClient,
    IOptions<RazorpayOptions> options,
    ILogger<RazorpayService> logger) : IRazorpayService
{
    private readonly RazorpayOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;
    public string KeyId => _options.KeyId;
    public string Currency => string.IsNullOrWhiteSpace(_options.Currency) ? "INR" : _options.Currency;

    /// <summary>Product prices are already INR, so paise is simply INR * 100 — no currency conversion.</summary>
    public long ToPaise(decimal amountInr) =>
        (long)Math.Round(amountInr * 100m, MidpointRounding.AwayFromZero);

    public async Task<RazorpayOrderResult> CreateOrderAsync(
        decimal amountInr,
        string receipt,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Razorpay is not configured. Set Razorpay:KeyId and Razorpay:KeySecret.");
        }

        var amountPaise = ToPaise(amountInr);
        if (amountPaise < 100)
        {
            throw new InvalidOperationException("Order amount must be at least ₹1.00 for Razorpay.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.razorpay.com/v1/orders");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.KeyId}:{_options.KeySecret}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

        var payload = new
        {
            amount = amountPaise,
            currency = Currency,
            receipt,
            payment_capture = 1,
            notes = new { source = "Bagly", customer_country = "India" },
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Razorpay order create failed ({(int)response.StatusCode}): {raw}");
        }

        var parsed = JsonSerializer.Deserialize<RazorpayOrderApiResponse>(raw)
            ?? throw new InvalidOperationException("Invalid Razorpay order response.");

        if (string.IsNullOrWhiteSpace(parsed.Id))
        {
            throw new InvalidOperationException("Razorpay did not return an order id.");
        }

        return new RazorpayOrderResult(
            parsed.Id,
            parsed.Amount,
            parsed.Currency ?? Currency,
            parsed.Receipt ?? receipt,
            parsed.Status ?? "created",
            raw);
    }

    public async Task<bool> TryRefundPaymentAsync(
        string razorpayPaymentId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(razorpayPaymentId))
        {
            logger.LogWarning(
                "Razorpay refund skipped for payment {PaymentId}: Razorpay not configured or payment id missing. Reason={Reason}.",
                razorpayPaymentId,
                reason);
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.razorpay.com/v1/payments/{razorpayPaymentId}/refund");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.KeyId}:{_options.KeySecret}"));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

            // Idempotency note in the refund reason so Razorpay's dashboard/support can correlate
            // retries of the same out-of-stock refund back to this order without side effects.
            var payload = new { speed = "normal", notes = new { reason, idempotency_note = "bagly-out-of-stock-refund" } };
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            logger.LogInformation(
                "Razorpay refund requested for payment {PaymentId}. Reason={Reason}.",
                razorpayPaymentId,
                reason);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Razorpay refund succeeded for payment {PaymentId}. Response={Response}.",
                    razorpayPaymentId,
                    raw);
                return true;
            }

            var errorDescription = TryExtractErrorDescription(raw);

            // Razorpay returns HTTP 400 BAD_REQUEST_ERROR with a description like "The payment has
            // been fully refunded already" if a refund is retried after it already succeeded (e.g.
            // our own retry, or a duplicate webhook). Treat that as success so we never downgrade
            // PaymentStatus from "Refunded" back to "Paid" on a retried verify call.
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest &&
                errorDescription is not null &&
                errorDescription.Contains("refund", StringComparison.OrdinalIgnoreCase) &&
                errorDescription.Contains("already", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(
                    "Razorpay refund for payment {PaymentId} was already refunded (treated as success). {Description}",
                    razorpayPaymentId,
                    errorDescription);
                return true;
            }

            logger.LogError(
                "Razorpay refund failed for payment {PaymentId}: HTTP {StatusCode}. {Response}. Reason={Reason}.",
                razorpayPaymentId,
                (int)response.StatusCode,
                raw,
                reason);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Razorpay refund threw an exception for payment {PaymentId}. Reason={Reason}.",
                razorpayPaymentId,
                reason);
            return false;
        }
    }

    /// <summary>Best-effort parse of Razorpay's <c>{ "error": { "description": "..." } }</c> shape. Returns null on any parse failure.</summary>
    private static string? TryExtractErrorDescription(string raw)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<RazorpayErrorResponse>(raw);
            return parsed?.Error?.Description;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public bool VerifyPaymentSignature(string razorpayOrderId, string razorpayPaymentId, string razorpaySignature)
    {
        if (!IsConfigured)
        {
            return false;
        }

        var payload = $"{razorpayOrderId}|{razorpayPaymentId}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.KeySecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(razorpaySignature.Trim().ToLowerInvariant()));
    }

    private sealed class RazorpayOrderApiResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("receipt")]
        public string? Receipt { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private sealed class RazorpayErrorResponse
    {
        [JsonPropertyName("error")]
        public RazorpayErrorDetail? Error { get; set; }
    }

    private sealed class RazorpayErrorDetail
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
