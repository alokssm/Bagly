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

    /// <summary>Admin diagnostic: login to Shiprocket and list pickup nicknames (never returns password).</summary>
    Task<ShiprocketConnectionProbeResult> ProbeConnectionAsync(CancellationToken cancellationToken = default);
}

public sealed record ShiprocketConnectionProbeResult(
    bool LoginOk,
    string? LoginError,
    string? ConfiguredPickup,
    bool ConfiguredPickupMatched,
    IReadOnlyList<string> PickupNicknames,
    string? PickupListError);

public sealed class ShiprocketService(
    IHttpClientFactory httpClientFactory,
    BaglyDbContext db,
    ShiprocketTokenStore tokenStore,
    IOptions<ShiprocketOptions> options,
    ILogger<ShiprocketService> logger) : IShiprocketService
{
    /// <summary>
    /// Explicit JsonPropertyName on payload types — do not rely on ASP.NET camelCase defaults.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly TimeZoneInfo IstTimeZone = ResolveIst();

    private readonly ShiprocketOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task TryCreateAdhocOrderForConfirmedOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            logger.LogWarning("Shiprocket skipped: order {OrderId} not found.", orderId);
            return;
        }

        if (!_options.Enabled)
        {
            await MarkSkippedAsync(
                order,
                "Shiprocket__Enabled is false. Set Shiprocket__Enabled=true plus Email/Password/PickupLocation on Render.",
                cancellationToken);
            return;
        }

        if (ShiprocketOptions.IsMissingCredential(_options.Email) ||
            ShiprocketOptions.IsMissingCredential(_options.Password))
        {
            await MarkSkippedAsync(
                order,
                "Shiprocket Email/Password missing or still SET_VIA_ENV placeholders.",
                cancellationToken);
            return;
        }

        if (ShiprocketOptions.IsPlaceholderPickup(_options.PickupLocation))
        {
            await MarkSkippedAsync(
                order,
                "Shiprocket__PickupLocation is missing, a SET_VIA_ENV placeholder, or 'test'. Set the exact nickname from Shiprocket → Settings → Pickup Addresses (case-sensitive).",
                cancellationToken);
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

        var pincode = NormalizePincode(order.Zip);
        if (pincode is null)
        {
            await MarkSkippedAsync(
                order,
                $"billing_pincode missing or not a valid 6-digit Indian PIN (raw={(string.IsNullOrWhiteSpace(order.Zip) ? "(null)" : order.Zip.Trim())})",
                cancellationToken);
            return;
        }

        if (order.Items.Count == 0)
        {
            await MarkSkippedAsync(order, "order has no line items", cancellationToken);
            return;
        }

        var pickup = _options.PickupLocation.Trim();

        try
        {
            var payload = BuildCreatePayload(order, phone, pincode.Value, pickup);
            var requestJson = JsonSerializer.Serialize(payload, JsonOptions);
            logger.LogInformation(
                "Shiprocket create starting for {OrderNumber}: pickup_location={Pickup}, payment_method={PaymentMethod}, phone=***{PhoneLast4}, pincode={Pincode}, items={ItemCount}, sub_total={SubTotal}, shipping_charges={Shipping}, request={RequestJson}.",
                order.OrderNumber,
                pickup,
                payload.PaymentMethod,
                phone.Length >= 4 ? phone[^4..] : phone,
                pincode.Value,
                order.Items.Count,
                payload.SubTotal,
                payload.ShippingCharges,
                Truncate(requestJson, 900));

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
            var detail = Truncate(
                $"{ex.Message} (pickup_location='{pickup}')",
                480);
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
                "Shiprocket create failed for {OrderNumber} (orderId={OrderId}, pickup={Pickup}). Customer order remains confirmed. Error persisted on order for admin.",
                order.OrderNumber,
                order.Id,
                pickup);
        }
    }

    public async Task<ShiprocketConnectionProbeResult> ProbeConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var configuredPickup = ShiprocketOptions.IsPlaceholderPickup(_options.PickupLocation)
            ? null
            : _options.PickupLocation.Trim();

        if (!_options.Enabled)
        {
            return new ShiprocketConnectionProbeResult(
                false,
                "Shiprocket__Enabled is false.",
                configuredPickup,
                false,
                [],
                null);
        }

        if (ShiprocketOptions.IsMissingCredential(_options.Email) ||
            ShiprocketOptions.IsMissingCredential(_options.Password))
        {
            return new ShiprocketConnectionProbeResult(
                false,
                "Shiprocket Email/Password missing or still SET_VIA_ENV placeholders.",
                configuredPickup,
                false,
                [],
                null);
        }

        try
        {
            tokenStore.Invalidate();
            await LoginAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return new ShiprocketConnectionProbeResult(
                false,
                Truncate(ex.Message, 300),
                configuredPickup,
                false,
                [],
                null);
        }

        try
        {
            var nicknames = await ListPickupNicknamesAsync(cancellationToken);
            var matched = !string.IsNullOrWhiteSpace(configuredPickup) &&
                          nicknames.Any(n => string.Equals(n, configuredPickup, StringComparison.Ordinal));
            return new ShiprocketConnectionProbeResult(
                true,
                null,
                configuredPickup,
                matched,
                nicknames,
                null);
        }
        catch (Exception ex)
        {
            return new ShiprocketConnectionProbeResult(
                true,
                null,
                configuredPickup,
                false,
                [],
                Truncate(ex.Message, 300));
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

    private ShiprocketCreatePayload BuildCreatePayload(Order order, string phone, int pincode, string pickup)
    {
        var isCod = string.Equals(order.PaymentProvider, "COD", StringComparison.OrdinalIgnoreCase);
        var paymentMethod = isCod ? "COD" : "Prepaid";

        // Match Shiprocket adhoc sample: selling_price as string; money totals as integers.
        var orderItems = order.Items.Select(i => new ShiprocketOrderItemPayload
        {
            Name = Truncate(
                string.IsNullOrWhiteSpace(i.Color) || string.Equals(i.Color, "Default", StringComparison.OrdinalIgnoreCase)
                    ? i.ProductName
                    : $"{i.ProductName} ({i.Color})",
                200),
            Sku = Truncate(string.IsNullOrWhiteSpace(i.ProductId) ? i.ProductName : i.ProductId, 50),
            Units = i.Quantity,
            SellingPrice = ToRupeeInt(i.UnitPrice).ToString(CultureInfo.InvariantCulture),
        }).ToList();

        var subTotal = ToRupeeInt(order.Subtotal);
        var shippingCharges = ToRupeeInt(order.Shipping);
        var codAmount = ToRupeeInt(order.Total);

        return new ShiprocketCreatePayload
        {
            OrderId = Truncate(order.OrderNumber, 50),
            OrderDate = FormatOrderDateIst(order.CreatedAt),
            PickupLocation = pickup,
            // Empty string => default Custom channel (official sample).
            ChannelId = "",
            BillingCustomerName = Truncate(order.FirstName.Trim(), 50),
            BillingLastName = Truncate(order.LastName.Trim(), 50),
            BillingAddress = Truncate(order.Address.Trim(), 190),
            BillingAddress2 = "",
            BillingCity = Truncate(order.City.Trim(), 30),
            // Shiprocket validates billing_pincode as a JSON number, exactly 6 digits.
            BillingPincode = pincode,
            BillingState = Truncate(order.State.Trim(), 50),
            BillingCountry = "India",
            BillingEmail = Truncate(order.Email.Trim(), 100),
            BillingPhone = phone,
            ShippingIsBilling = true,
            OrderItems = orderItems,
            PaymentMethod = paymentMethod,
            // COD collectable amount (order total incl. shipping).
            Cod = isCod ? codAmount : null,
            ShippingCharges = shippingCharges,
            GiftwrapCharges = 0,
            TransactionCharges = 0,
            TotalDiscount = 0,
            SubTotal = subTotal,
            Length = _options.DefaultLength,
            Breadth = _options.DefaultBreadth,
            Height = _options.DefaultHeight,
            Weight = _options.DefaultWeightKg,
        };
    }

    private static int ToRupeeInt(decimal value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);

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
        ShiprocketCreatePayload payload,
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
        ShiprocketCreatePayload payload,
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
                $"Shiprocket create failed HTTP {(int)response.StatusCode}. Body: {Truncate(raw, 480)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var root = doc.RootElement;

        // Shiprocket sometimes returns HTTP 200 with status_code >= 400 and no order_id.
        if (TryReadStatusCode(root, out var apiStatus) && apiStatus >= 400)
        {
            var apiMessage = TryReadMessage(root) ?? Truncate(raw);
            throw new InvalidOperationException(
                $"Shiprocket create rejected status_code={apiStatus}: {apiMessage}");
        }

        var orderId = ReadId(root, "order_id");
        var shipmentId = ReadId(root, "shipment_id");
        var status = root.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String
            ? statusEl.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(orderId))
        {
            var apiMessage = TryReadMessage(root);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(apiMessage)
                    ? $"Shiprocket create succeeded but order_id was missing. Body: {Truncate(raw)}"
                    : $"Shiprocket create returned no order_id: {apiMessage}. Body: {Truncate(raw)}");
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

    private async Task<IReadOnlyList<string>> ListPickupNicknamesAsync(CancellationToken cancellationToken)
    {
        var token = tokenStore.GetValidToken() ?? await LoginAsync(cancellationToken);
        var client = httpClientFactory.CreateClient("Shiprocket");
        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/external/settings/company/pickup");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Shiprocket pickup list failed HTTP {(int)response.StatusCode}. Body: {Truncate(raw)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var nicknames = new List<string>();
        CollectPickupNicknames(doc.RootElement, nicknames);
        return nicknames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CollectPickupNicknames(JsonElement el, List<string> nicknames)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                if (el.TryGetProperty("pickup_location", out var pl) &&
                    pl.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(pl.GetString()))
                {
                    nicknames.Add(pl.GetString()!);
                }

                foreach (var prop in el.EnumerateObject())
                {
                    CollectPickupNicknames(prop.Value, nicknames);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                {
                    CollectPickupNicknames(item, nicknames);
                }

                break;
        }
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

    /// <summary>
    /// Normalize to digits and require a 6-digit Indian PIN.
    /// Shiprocket rejects non-numeric / wrong-length billing_pincode with HTTP 422.
    /// </summary>
    internal static int? NormalizePincode(string? zip)
    {
        if (string.IsNullOrWhiteSpace(zip))
        {
            return null;
        }

        var digits = new string(zip.Where(char.IsDigit).ToArray());
        if (digits.Length != 6)
        {
            return null;
        }

        // Indian PINs are 100000–999999 (never leading zero in practice; first digit 1–9).
        if (digits[0] == '0')
        {
            return null;
        }

        return int.Parse(digits, CultureInfo.InvariantCulture);
    }

    private static bool TryReadStatusCode(JsonElement root, out int statusCode)
    {
        statusCode = 0;
        if (!root.TryGetProperty("status_code", out var el))
        {
            return false;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetInt32(out statusCode),
            JsonValueKind.String => int.TryParse(el.GetString(), out statusCode),
            _ => false,
        };
    }

    private static string? TryReadMessage(JsonElement root)
    {
        if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
        {
            return msg.GetString();
        }

        if (root.TryGetProperty("error", out var err))
        {
            return err.ValueKind switch
            {
                JsonValueKind.String => err.GetString(),
                JsonValueKind.Object or JsonValueKind.Array => Truncate(err.GetRawText(), 300),
                _ => null,
            };
        }

        if (root.TryGetProperty("errors", out var errors))
        {
            return Truncate(errors.GetRawText(), 300);
        }

        return null;
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

    private sealed class ShiprocketCreatePayload
    {
        [JsonPropertyName("order_id")]
        public string OrderId { get; set; } = string.Empty;

        [JsonPropertyName("order_date")]
        public string OrderDate { get; set; } = string.Empty;

        [JsonPropertyName("pickup_location")]
        public string PickupLocation { get; set; } = string.Empty;

        [JsonPropertyName("channel_id")]
        public string ChannelId { get; set; } = string.Empty;

        [JsonPropertyName("billing_customer_name")]
        public string BillingCustomerName { get; set; } = string.Empty;

        [JsonPropertyName("billing_last_name")]
        public string BillingLastName { get; set; } = string.Empty;

        [JsonPropertyName("billing_address")]
        public string BillingAddress { get; set; } = string.Empty;

        [JsonPropertyName("billing_address_2")]
        public string BillingAddress2 { get; set; } = string.Empty;

        [JsonPropertyName("billing_city")]
        public string BillingCity { get; set; } = string.Empty;

        [JsonPropertyName("billing_pincode")]
        public int BillingPincode { get; set; }

        [JsonPropertyName("billing_state")]
        public string BillingState { get; set; } = string.Empty;

        [JsonPropertyName("billing_country")]
        public string BillingCountry { get; set; } = string.Empty;

        [JsonPropertyName("billing_email")]
        public string BillingEmail { get; set; } = string.Empty;

        [JsonPropertyName("billing_phone")]
        public string BillingPhone { get; set; } = string.Empty;

        [JsonPropertyName("shipping_is_billing")]
        public bool ShippingIsBilling { get; set; }

        [JsonPropertyName("order_items")]
        public List<ShiprocketOrderItemPayload> OrderItems { get; set; } = [];

        [JsonPropertyName("payment_method")]
        public string PaymentMethod { get; set; } = string.Empty;

        [JsonPropertyName("cod")]
        public int? Cod { get; set; }

        [JsonPropertyName("shipping_charges")]
        public int ShippingCharges { get; set; }

        [JsonPropertyName("giftwrap_charges")]
        public int GiftwrapCharges { get; set; }

        [JsonPropertyName("transaction_charges")]
        public int TransactionCharges { get; set; }

        [JsonPropertyName("total_discount")]
        public int TotalDiscount { get; set; }

        [JsonPropertyName("sub_total")]
        public int SubTotal { get; set; }

        [JsonPropertyName("length")]
        public double Length { get; set; }

        [JsonPropertyName("breadth")]
        public double Breadth { get; set; }

        [JsonPropertyName("height")]
        public double Height { get; set; }

        [JsonPropertyName("weight")]
        public double Weight { get; set; }
    }

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
