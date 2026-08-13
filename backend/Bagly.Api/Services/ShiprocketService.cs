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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ShiprocketOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task TryCreateAdhocOrderForConfirmedOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            logger.LogDebug("Shiprocket skipped for order {OrderId}: integration disabled.", orderId);
            return;
        }

        if (!IsConfigured)
        {
            logger.LogWarning(
                "Shiprocket skipped for order {OrderId}: Enabled but Email/Password/PickupLocation are not configured.",
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
            logger.LogInformation(
                "Shiprocket skipped for {OrderNumber}: status is {Status}, not Confirmed.",
                order.OrderNumber,
                order.Status);
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

        if (!string.Equals(order.Country?.Trim(), "India", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Shiprocket skipped for {OrderNumber}: country is {Country} (India only in v1).",
                order.OrderNumber,
                order.Country);
            return;
        }

        var phone = NormalizePhone(order.Phone);
        if (string.IsNullOrWhiteSpace(phone))
        {
            logger.LogWarning(
                "Shiprocket skipped for {OrderNumber}: phone is missing. Collect phone on checkout to enable shipment creation.",
                order.OrderNumber);
            return;
        }

        if (order.Items.Count == 0)
        {
            logger.LogWarning("Shiprocket skipped for {OrderNumber}: order has no line items.", order.OrderNumber);
            return;
        }

        try
        {
            var payload = BuildCreatePayload(order, phone);
            var result = await CreateAdhocOrderWithAuthRetryAsync(payload, cancellationToken);

            order.ShiprocketOrderId = result.OrderId;
            order.ShiprocketShipmentId = result.ShipmentId;
            order.ShiprocketStatus = result.Status;
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
            logger.LogError(
                ex,
                "Shiprocket create failed for {OrderNumber} (orderId={OrderId}). Customer order remains confirmed.",
                order.OrderNumber,
                order.Id);
        }
    }

    private object BuildCreatePayload(Order order, string phone)
    {
        var paymentMethod = string.Equals(order.PaymentProvider, "COD", StringComparison.OrdinalIgnoreCase)
            ? "COD"
            : "Prepaid";

        var orderItems = order.Items.Select(i => new
        {
            name = string.IsNullOrWhiteSpace(i.Color) || string.Equals(i.Color, "Default", StringComparison.OrdinalIgnoreCase)
                ? i.ProductName
                : $"{i.ProductName} ({i.Color})",
            sku = i.ProductId,
            units = i.Quantity,
            selling_price = i.UnitPrice.ToString("0.##"),
        }).ToList();

        // Product has no weight/dimensions yet — use configured package defaults for the whole order.
        // Follow-up: multi-seller pickup locations (v1 uses a single platform warehouse nickname).
        var payload = new Dictionary<string, object?>
        {
            ["order_id"] = order.OrderNumber,
            ["order_date"] = order.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
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

        if (paymentMethod == "COD")
        {
            // Amount to collect on delivery (order total including shipping).
            payload["cod"] = order.Total;
        }

        return payload;
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
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

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

    /// <summary>Normalize to digits; prefer last 10 for Indian mobiles.</summary>
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

        return digits.Length >= 10 ? digits : digits;
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
}
