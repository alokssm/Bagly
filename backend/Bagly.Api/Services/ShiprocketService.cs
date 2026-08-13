using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bagly.Api.Data;
using Bagly.Api.Models;
using Bagly.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Services;

public interface IShiprocketService
{
    bool IsConfigured { get; }

    /// <summary>
    /// Best-effort Shiprocket adhoc create for a confirmed Bagly order.
    /// Never throws to callers that only log — failures are logged and order checkout is unaffected.
    /// Idempotent when <see cref="Order.ShiprocketOrderId"/> is already set.
    /// </summary>
    Task TryCreateAdhocOrderForConfirmedOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}

public sealed class ShiprocketService(
    IHttpClientFactory httpClientFactory,
    BaglyDbContext db,
    ShiprocketTokenStore tokenStore,
    IOptions<ShiprocketOptions> options,
    ILogger<ShiprocketService> logger) : IShiprocketService
{
    /// <summary>
    /// Snake_case for nested POCOs. Dictionary keys in <see cref="BuildCreatePayload"/> are already
    /// snake_case and are not rewritten (DictionaryKeyPolicy is null by default).
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly TimeZoneInfo IstTimeZone = ResolveIst();

    private readonly ShiprocketOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task TryCreateAdhocOrderForConfirmedOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            // Information (not Debug): Render/production default min level hides Debug, and operators
            // need to see why nothing appears on the Shiprocket dashboard.
            logger.LogInformation(
                "Shiprocket skipped for order {OrderId}: Shiprocket__Enabled is false. Set Shiprocket__Enabled=true plus Email/Password/PickupLocation on Render.",
                orderId);
            return;
        }

        if (!IsConfigured)
        {
            logger.LogWarning(
                "Shiprocket skipped for order {OrderId}: Enabled but Email/Password/PickupLocation are not configured (or still SET_VIA_ENV placeholders).",
                orderId);
            return;
        }

        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            logger.LogWarning("Shiprocket skipped: order {OrderId} not found.", orderId);
            return;
        }

        if (!string.Equals(order.Status, "Confirmed", StringComparison.OrdinalIgnoreCase))
        {
            await MarkSkippedAsync(
                order,
                $"status is {order.Status}, not Confirmed",
                cancellationToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(order.ShiprocketOrderId))
        {
            logger.LogDebug(
                "Shiprocket skipped for {OrderNumber}: already created (ShiprocketOrderId={ShiprocketOrderId}).",
                order.OrderNumber,
                order.ShiprocketOrderId);
            return;
        }

        if (!IsIndiaCountry(order.Country))
        {
            await MarkSkippedAsync(
                order,
                $"country is '{order.Country}' (India/IN only in v1)",
                cancellationToken);
            return;
        }

        var phone = NormalizePhone(order.Phone);
        if (string.IsNullOrWhiteSpace(phone))
        {
            await MarkSkippedAsync(
                order,
                $"phone missing or not a valid 10-digit Indian mobile (raw={(string.IsNullOrWhiteSpace(order.Phone) ? "(null)" : order.Phone.Trim())})",
                cancellationToken);
            return;
        }

        if (order.Items.Count == 0)
        {
            await MarkSkippedAsync(order, "order has no line items", cancellationToken);
            return;
        }

        try
        {
            var payload = BuildCreatePayload(order, phone);
            var result = await CreateAdhocOrderWithAuthRetryAsync(payload, cancellationToken);

            order.ShiprocketOrderId = result.OrderId;
            order.ShiprocketShipmentId = result.ShipmentId;
            order.ShiprocketStatus = result.Status ?? "NEW";
            order.ShiprocketLastError = null;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Shiprocket order created for {OrderNumber}: shiprocketOrderId={ShiprocketOrderId}, shipmentId={ShipmentId}, status={Status}.",
                order.OrderNumber,
                order.ShiprocketOrderId,
                order.ShiprocketShipmentId,
                order.ShiprocketStatus);
        }
        catch (Exception ex)
        {
            var detail = Truncate(ex.Message, 480);
            order.ShiprocketStatus = "Error";
            order.ShiprocketLastError = detail;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                logger.LogError(
                    saveEx,
                    "Shiprocket failed for {OrderNumber} and could not persist ShiprocketLastError.",
                    order.OrderNumber);
            }

            logger.LogError(
                ex,
                "Shiprocket create failed for {OrderNumber} (orderId={OrderId}). Customer order remains confirmed. Error persisted on order for admin.",
                order.OrderNumber,
                order.Id);
        }
    }

    private async Task MarkSkippedAsync(Order order, string reason, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Shiprocket skipped for {OrderNumber}: {Reason}.",
            order.OrderNumber,
            reason);

        // Don't overwrite a prior API error if we somehow re-enter after a failed create.
        if (string.Equals(order.ShiprocketStatus, "Error", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(order.ShiprocketLastError))
        {
            return;
        }

        order.ShiprocketStatus = "Skipped";
        order.ShiprocketLastError = Truncate(reason, 480);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsIndiaCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return false;
        }

        var value = country.Trim();
        return string.Equals(value, "India", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "IN", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "IND", StringComparison.OrdinalIgnoreCase);
    }

    private object BuildCreatePayload(Order order, string phone)
    {
        var paymentMethod = string.Equals(order.PaymentProvider, "COD", StringComparison.OrdinalIgnoreCase)
            ? "COD"
            : "Prepaid";

        // Explicit snake_case property names — do not rely on ASP.NET camelCase defaults.
        var orderItems = order.Items.Select(i => new ShiprocketOrderItemPayload
        {
            Name = string.IsNullOrWhiteSpace(i.Color) || string.Equals(i.Color, "Default", StringComparison.OrdinalIgnoreCase)
                ? i.ProductName
                : $"{i.ProductName} ({i.Color})",
            Sku = i.ProductId,
            Units = i.Quantity,
            SellingPrice = i.UnitPrice.ToString("0.##", CultureInfo.InvariantCulture),
        }).ToList();

        // Product has no weight/dimensions yet — use configured package defaults for the whole order.
        // Follow-up: multi-seller pickup locations (v1 uses a single platform warehouse nickname).
        var payload = new Dictionary<string, object?>
        {
            ["order_id"] = order.OrderNumber,
            ["order_date"] = FormatOrderDateIst(order.CreatedAt),
            ["pickup_location"] = _options.PickupLocation.Trim(),
            ["billing_customer_name"] = order.FirstName.Trim(),
            ["billing_last_name"] = order.LastName.Trim(),
            ["billing_address"] = order.Address.Trim(),
            ["billing_address_2"] = "",
            ["billing_city"] = order.City.Trim(),
            ["billing_pincode"] = order.Zip.Trim(),
            ["billing_state"] = order.State.Trim(),
            ["billing_country"] = "India",
            ["billing_email"] = order.Email.Trim(),
            ["billing_phone"] = phone,
            ["shipping_is_billing"] = true,
            ["order_items"] = orderItems,
            ["payment_method"] = paymentMethod,
            ["shipping_charges"] = order.Shipping,
            ["sub_total"] = order.Subtotal,
            ["length"] = _options.DefaultLength,
            ["breadth"] = _options.DefaultBreadth,
            ["height"] = _options.DefaultHeight,
            ["weight"] = _options.DefaultWeightKg,
        };

        return payload;
    }

    private static string FormatOrderDateIst(DateTime createdAtUtc)
    {
        var utc = createdAtUtc.Kind switch
        {
            DateTimeKind.Utc => createdAtUtc,
            DateTimeKind.Local => createdAtUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc),
        };
        var ist = TimeZoneInfo.ConvertTimeFromUtc(utc, IstTimeZone);
        return ist.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private static TimeZoneInfo ResolveIst()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        }
    }

    private async Task<ShiprocketCreateResult> CreateAdhocOrderWithAuthRetryAsync(
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CreateAdhocOrderAsync(payload, forceLogin: false, cancellationToken);
        }
        catch (ShiprocketAuthException)
        {
            tokenStore.Invalidate();
            return await CreateAdhocOrderAsync(payload, forceLogin: true, cancellationToken);
        }
    }

    private async Task<ShiprocketCreateResult> CreateAdhocOrderAsync(
        object payload,
        bool forceLogin,
        CancellationToken cancellationToken)
    {
        var token = forceLogin
            ? await LoginAsync(cancellationToken)
            : tokenStore.GetValidToken() ?? await LoginAsync(cancellationToken);

        var client = httpClientFactory.CreateClient("Shiprocket");
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/external/orders/create/adhoc");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenStore.Invalidate();
            throw new ShiprocketAuthException($"Shiprocket create returned 401. Body: {Truncate(raw)}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Shiprocket create failed HTTP {(int)response.StatusCode}. Body: {Truncate(raw)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var root = doc.RootElement;

        var orderId = ReadId(root, "order_id");
        var shipmentId = ReadId(root, "shipment_id");
        var status = root.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String
            ? statusEl.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(orderId))
        {
            throw new InvalidOperationException(
                $"Shiprocket create succeeded but order_id was missing. Body: {Truncate(raw)}");
        }

        return new ShiprocketCreateResult(orderId, shipmentId, status);
    }

    private async Task<string> LoginAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("Shiprocket");
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/external/auth/login");
        // Auth body uses camelCase email/password (Shiprocket login accepts these).
        var body = new { email = _options.Email.Trim(), password = _options.Password };
        request.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new ShiprocketAuthException(
                $"Shiprocket login failed HTTP {(int)response.StatusCode}. Body: {Truncate(raw)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        if (!doc.RootElement.TryGetProperty("token", out var tokenEl) ||
            tokenEl.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(tokenEl.GetString()))
        {
            throw new ShiprocketAuthException($"Shiprocket login response missing token. Body: {Truncate(raw)}");
        }

        var token = tokenEl.GetString()!;
        tokenStore.SetToken(token);
        return token;
    }

    /// <summary>Normalize to digits; require 10-digit Indian mobile (strip leading 91 when present).</summary>
    internal static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return null;
        }

        if (digits.Length > 10 && digits.StartsWith("91", StringComparison.Ordinal))
        {
            digits = digits[^10..];
        }
        else if (digits.Length > 10)
        {
            digits = digits[^10..];
        }

        // Shiprocket requires a 10-digit billing_phone; shorter values cause create failures.
        return digits.Length == 10 ? digits : null;
    }

    private static string? ReadId(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetInt64(out var n) ? n.ToString() : el.GetRawText(),
            JsonValueKind.String => el.GetString(),
            _ => null,
        };
    }

    private static string Truncate(string value, int max = 500) =>
        value.Length <= max ? value : value[..max] + "…";

    private sealed record ShiprocketCreateResult(string OrderId, string? ShipmentId, string? Status);

    private sealed class ShiprocketAuthException(string message) : Exception(message);

    private sealed class ShiprocketOrderItemPayload
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("sku")]
        public string Sku { get; set; } = string.Empty;

        [JsonPropertyName("units")]
        public int Units { get; set; }

        [JsonPropertyName("selling_price")]
        public string SellingPrice { get; set; } = string.Empty;
    }
}
