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
    decimal UsdToInrRate { get; }
    long ToPaise(decimal amountInr);
    decimal ToInr(decimal amountUsd);
    Task<RazorpayOrderResult> CreateOrderAsync(decimal amountInr, string receipt, CancellationToken cancellationToken = default);
    bool VerifyPaymentSignature(string razorpayOrderId, string razorpayPaymentId, string razorpaySignature);
}

public record RazorpayOrderResult(string Id, long Amount, string Currency, string Receipt, string Status, string RawJson);

public class RazorpayService(HttpClient httpClient, IOptions<RazorpayOptions> options) : IRazorpayService
{
    private readonly RazorpayOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;
    public string KeyId => _options.KeyId;
    public string Currency => string.IsNullOrWhiteSpace(_options.Currency) ? "INR" : _options.Currency;
    public decimal UsdToInrRate => _options.UsdToInrRate <= 0 ? 83m : _options.UsdToInrRate;

    public long ToPaise(decimal amountInr) =>
        (long)Math.Round(amountInr * 100m, MidpointRounding.AwayFromZero);

    public decimal ToInr(decimal amountUsd) =>
        Math.Round(amountUsd * UsdToInrRate, 2, MidpointRounding.AwayFromZero);

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
}
