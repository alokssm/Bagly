using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bagly.Api.Data;
using Bagly.Api.Models;
using Bagly.Api.Options;
using Bagly.Api.Shipping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Services;

public interface IShiprocketService
{
    bool IsConfigured { get; }

    /// <summary>
    /// Best-effort Shiprocket adhoc create(s) for a confirmed Bagly order.
    /// Groups line items by product pickup nickname and creates one adhoc order per group.
    /// Idempotent per pickup group; retries only failed groups.
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
    IShiprocketApiLogService apiLogs,
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
    private static readonly Regex PickupSlugUnsafe = new(@"[^a-zA-Z0-9_-]+", RegexOptions.Compiled);

    private readonly ShiprocketOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task TryCreateAdhocOrderForConfirmedOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .Include(o => o.ShiprocketShipments)
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

        // Legacy: single Order.ShiprocketOrderId with no child rows = already fully created under one pickup.
        if (!string.IsNullOrWhiteSpace(order.ShiprocketOrderId) && order.ShiprocketShipments.Count == 0)
        {
            logger.LogDebug(
                "Shiprocket skipped for {OrderNumber}: legacy ShiprocketOrderId={ShiprocketOrderId} (no multi-pickup rows).",
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

        var rawZip = string.IsNullOrWhiteSpace(order.Zip) ? "(null)" : order.Zip.Trim();
        var pincode = NormalizePincode(order.Zip);
        if (pincode is null)
        {
            await MarkSkippedAsync(
                order,
                $"Order zip '{rawZip}' is not a valid 6-digit PIN",
                cancellationToken);
            return;
        }

        var normalizedZip = pincode.Value.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(order.Zip, normalizedZip, StringComparison.Ordinal))
        {
            order.Zip = normalizedZip;
        }

        if (order.Items.Count == 0)
        {
            await MarkSkippedAsync(order, "order has no line items", cancellationToken);
            return;
        }

        var defaultPickup = _options.PickupLocation.Trim();
        var productIds = order.Items
            .Select(i => i.ProductId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var productRows = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.ShiprocketPickupLocation,
                p.UseDefaultPackageSize,
                p.WeightKg,
                p.LengthCm,
                p.BreadthCm,
                p.HeightCm,
            })
            .ToListAsync(cancellationToken);

        var productPickups = productRows.ToDictionary(
            p => p.Id,
            p => p.ShiprocketPickupLocation,
            StringComparer.Ordinal);

        var productPackages = productRows.ToDictionary(
            p => p.Id,
            p => new ShiprocketPackageResolver.ProductPackageInfo(
                p.UseDefaultPackageSize,
                p.WeightKg,
                p.LengthCm,
                p.BreadthCm,
                p.HeightCm),
            StringComparer.Ordinal);

        // Case-sensitive group key = exact Shiprocket nickname.
        var groups = order.Items
            .GroupBy(item =>
            {
                productPickups.TryGetValue(item.ProductId, out var productPickup);
                return string.IsNullOrWhiteSpace(productPickup) ? defaultPickup : productPickup.Trim();
            }, StringComparer.Ordinal)
            .Select(g => new PickupGroup(g.Key, g.ToList()))
            .OrderBy(g => g.Pickup, StringComparer.Ordinal)
            .ToList();

        if (groups.Count == 0)
        {
            await MarkSkippedAsync(order, "order has no line items after grouping", cancellationToken);
            return;
        }

        var existingByPickup = order.ShiprocketShipments
            .ToDictionary(s => s.PickupLocation, StringComparer.Ordinal);

        var anyFailure = false;
        string? lastError = null;
        OrderShiprocketShipment? firstSuccess = null;

        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            if (existingByPickup.TryGetValue(group.Pickup, out var existing) &&
                !string.IsNullOrWhiteSpace(existing.ShiprocketOrderId))
            {
                firstSuccess ??= existing;
                logger.LogDebug(
                    "Shiprocket skip group {Pickup} for {OrderNumber}: already created ({ShiprocketOrderId}).",
                    group.Pickup,
                    order.OrderNumber,
                    existing.ShiprocketOrderId);
                continue;
            }

            var shipment = existing ?? new OrderShiprocketShipment
            {
                OrderId = order.Id,
                PickupLocation = group.Pickup,
                CreatedAt = DateTime.UtcNow,
            };

            if (existing is null)
            {
                db.OrderShiprocketShipments.Add(shipment);
                order.ShiprocketShipments.Add(shipment);
                existingByPickup[group.Pickup] = shipment;
            }

            // Full shipping + full COD on first group only (collect once); 0 on others.
            var chargeShippingAndCod = groupIndex == 0;
            try
            {
                var payload = BuildCreatePayload(
                    order,
                    group.Items,
                    productPackages,
                    phone,
                    pincode.Value,
                    group.Pickup,
                    chargeShippingAndCod,
                    chargeShippingAndCod);
                var requestJson = SerializeCreatePayload(payload);
                var pincodeJsonToken = ExtractBillingPincodeJsonToken(requestJson);
                if (!IsSixDigitJsonNumberToken(pincodeJsonToken))
                {
                    throw new InvalidOperationException(
                        $"Shiprocket billing_pincode serialization invalid (expected 6-digit JSON number). rawZip='{rawZip}', normalized={pincode.Value}, jsonToken={pincodeJsonToken ?? "(missing)"}");
                }

                logger.LogInformation(
                    "Shiprocket create starting for {OrderNumber}/{Pickup}: order_id={ShiprocketClientOrderId}, payment_method={PaymentMethod}, phone=***{PhoneLast4}, rawZip={RawZip}, billing_pincode_json={PincodeJson}, items={ItemCount}, sub_total={SubTotal}, shipping_charges={Shipping}, cod={Cod}, request={RequestJson}.",
                    order.OrderNumber,
                    group.Pickup,
                    payload.OrderId,
                    payload.PaymentMethod,
                    phone.Length >= 4 ? phone[^4..] : phone,
                    rawZip,
                    pincodeJsonToken,
                    group.Items.Count,
                    payload.SubTotal,
                    payload.ShippingCharges,
                    payload.Cod,
                    Truncate(requestJson, 900));

                var result = await CreateAdhocOrderWithAuthRetryAsync(
                    payload, order.Id, shipment.Id, cancellationToken);

                shipment.ShiprocketOrderId = result.OrderId;
                shipment.ShiprocketShipmentId = result.ShipmentId;
                shipment.Status = result.Status ?? "NEW";
                shipment.LastError = null;
                shipment.UpdatedAt = DateTime.UtcNow;
                firstSuccess ??= shipment;

                logger.LogInformation(
                    "Shiprocket order created for {OrderNumber}/{Pickup}: shiprocketOrderId={ShiprocketOrderId}, shipmentId={ShipmentId}, status={Status}.",
                    order.OrderNumber,
                    group.Pickup,
                    shipment.ShiprocketOrderId,
                    shipment.ShiprocketShipmentId,
                    shipment.Status);
            }
            catch (Exception ex)
            {
                anyFailure = true;
                var detail = Truncate(
                    $"{ex.Message} (pickup_location='{group.Pickup}')",
                    480);
                lastError = detail;
                shipment.Status = "Error";
                shipment.LastError = detail;
                shipment.UpdatedAt = DateTime.UtcNow;

                logger.LogError(
                    ex,
                    "Shiprocket create failed for {OrderNumber}/{Pickup} (orderId={OrderId}). Customer order remains confirmed.",
                    order.OrderNumber,
                    group.Pickup,
                    order.Id);
            }

            // Persist per group so a crash mid-run does not re-create a successful adhoc order.
            try
            {
                SyncOrderPrimaryShiprocketFields(order, firstSuccess, anyFailure, lastError);
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                logger.LogError(
                    saveEx,
                    "Shiprocket could not persist shipment result for {OrderNumber}/{Pickup}.",
                    order.OrderNumber,
                    group.Pickup);
            }
        }

        SyncOrderPrimaryShiprocketFields(order, firstSuccess, anyFailure, lastError);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception saveEx)
        {
            logger.LogError(
                saveEx,
                "Shiprocket could not persist final shipment summary for {OrderNumber}.",
                order.OrderNumber);
        }
    }

    private static void SyncOrderPrimaryShiprocketFields(
        Order order,
        OrderShiprocketShipment? firstSuccessHint,
        bool anyFailure,
        string? lastError)
    {
        var firstSuccess = firstSuccessHint ??
            order.ShiprocketShipments
                .Where(s => !string.IsNullOrWhiteSpace(s.ShiprocketOrderId))
                .OrderBy(s => s.CreatedAt)
                .FirstOrDefault();

        if (firstSuccess is not null)
        {
            order.ShiprocketOrderId = firstSuccess.ShiprocketOrderId;
            order.ShiprocketShipmentId = firstSuccess.ShiprocketShipmentId;
            order.ShiprocketStatus = firstSuccess.Status ?? "NEW";
        }

        if (anyFailure)
        {
            order.ShiprocketStatus = "Error";
            order.ShiprocketLastError = lastError;
        }
        else
        {
            order.ShiprocketLastError = null;
            if (firstSuccess is not null)
            {
                order.ShiprocketStatus = firstSuccess.Status ?? "NEW";
            }
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

    private ShiprocketCreatePayload BuildCreatePayload(
        Order order,
        IReadOnlyList<OrderItem> items,
        IReadOnlyDictionary<string, ShiprocketPackageResolver.ProductPackageInfo> productPackages,
        string phone,
        int pincode,
        string pickup,
        bool includeShippingCharges,
        bool includeCodAmount)
    {
        var isCod = string.Equals(order.PaymentProvider, "COD", StringComparison.OrdinalIgnoreCase);
        var paymentMethod = isCod ? "COD" : "Prepaid";

        var orderItems = items.Select(i => new ShiprocketOrderItemPayload
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

        var lineSubtotal = items.Sum(i => i.UnitPrice * i.Quantity);
        var subTotal = ToRupeeInt(lineSubtotal);
        var shippingCharges = includeShippingCharges ? ToRupeeInt(order.Shipping) : 0;
        // COD collect once on first shipment: full Bagly order total (incl. shipping).
        var codAmount = includeCodAmount ? ToRupeeInt(order.Total) : 0;

        var clientOrderId = BuildClientOrderId(order.OrderNumber, pickup);

        // Package: sum(weight×qty); max L/B/H. Default-flagged lines use ShiprocketOptions defaults.
        var package = ShiprocketPackageResolver.ResolveForLines(
            items.Select(i =>
            {
                productPackages.TryGetValue(i.ProductId, out var info);
                return (i.Quantity, (ShiprocketPackageResolver.ProductPackageInfo?)info);
            }),
            _options);

        return new ShiprocketCreatePayload
        {
            OrderId = Truncate(clientOrderId, 50),
            OrderDate = FormatOrderDateIst(order.CreatedAt),
            PickupLocation = pickup,
            ChannelId = "",
            BillingCustomerName = Truncate(order.FirstName.Trim(), 50),
            BillingLastName = Truncate(order.LastName.Trim(), 50),
            BillingAddress = Truncate(order.Address.Trim(), 190),
            BillingAddress2 = "",
            BillingCity = Truncate(order.City.Trim(), 30),
            BillingPincode = pincode,
            BillingState = Truncate(order.State.Trim(), 50),
            BillingCountry = "India",
            BillingEmail = Truncate(order.Email.Trim(), 100),
            BillingPhone = phone,
            ShippingIsBilling = true,
            ShippingPincode = null,
            OrderItems = orderItems,
            PaymentMethod = paymentMethod,
            Cod = isCod ? codAmount : null,
            ShippingCharges = shippingCharges,
            GiftwrapCharges = 0,
            TransactionCharges = 0,
            TotalDiscount = 0,
            SubTotal = subTotal,
            Length = package.Length,
            Breadth = package.Breadth,
            Height = package.Height,
            Weight = package.WeightKg,
        };
    }

    /// <summary>Shiprocket requires unique order_id per adhoc create: {BaglyOrderNumber}-{pickupSlug}.</summary>
    internal static string BuildClientOrderId(string orderNumber, string pickup)
    {
        var slug = ToPickupSlug(pickup);
        var baseId = string.IsNullOrWhiteSpace(orderNumber) ? "BG" : orderNumber.Trim();
        return $"{baseId}-{slug}";
    }

    internal static string ToPickupSlug(string pickup)
    {
        var trimmed = string.IsNullOrWhiteSpace(pickup) ? "pickup" : pickup.Trim();
        var slug = PickupSlugUnsafe.Replace(trimmed, "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "pickup" : slug;
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
        Guid orderId,
        Guid baglyShipmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CreateAdhocOrderAsync(
                payload, forceLogin: false, orderId, baglyShipmentId, cancellationToken);
        }
        catch (ShiprocketAuthException)
        {
            tokenStore.Invalidate();
            return await CreateAdhocOrderAsync(
                payload, forceLogin: true, orderId, baglyShipmentId, cancellationToken);
        }
    }

    private async Task<ShiprocketCreateResult> CreateAdhocOrderAsync(
        ShiprocketCreatePayload payload,
        bool forceLogin,
        Guid orderId,
        Guid baglyShipmentId,
        CancellationToken cancellationToken)
    {
        var token = forceLogin
            ? await LoginAsync(cancellationToken)
            : tokenStore.GetValidToken() ?? await LoginAsync(cancellationToken);

        const string path = "v1/external/orders/create/adhoc";
        var client = httpClientFactory.CreateClient("Shiprocket");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var json = SerializeCreatePayload(payload);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        await apiLogs.LogAsync(
            "CreateAdhoc",
            "POST",
            path,
            requestJson: json,
            responseStatus: (int)response.StatusCode,
            responseJson: raw,
            orderId: orderId,
            shipmentId: baglyShipmentId,
            cancellationToken: cancellationToken);

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

        if (TryReadStatusCode(root, out var apiStatus) && apiStatus >= 400)
        {
            var apiMessage = TryReadMessage(root) ?? Truncate(raw);
            throw new InvalidOperationException(
                $"Shiprocket create rejected status_code={apiStatus}: {apiMessage}");
        }

        var orderIdResult = ReadId(root, "order_id");
        var shipmentId = ReadId(root, "shipment_id");
        var status = root.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String
            ? statusEl.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(orderIdResult))
        {
            var apiMessage = TryReadMessage(root);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(apiMessage)
                    ? $"Shiprocket create succeeded but order_id was missing. Body: {Truncate(raw)}"
                    : $"Shiprocket create returned no order_id: {apiMessage}. Body: {Truncate(raw)}");
        }

        return new ShiprocketCreateResult(orderIdResult, shipmentId, status);
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

        return digits.Length == 10 ? digits : null;
    }

    /// <summary>
    /// Normalize Zip to a 6-digit Indian PIN (digits only).
    /// If more than 6 digits are present (e.g. zip+phone paste), take the last 6 when valid.
    /// </summary>
    internal static int? NormalizePincode(string? zip)
    {
        if (string.IsNullOrWhiteSpace(zip))
        {
            return null;
        }

        var digits = new string(zip.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return null;
        }

        if (digits.Length > 6)
        {
            digits = digits[^6..];
        }

        if (digits.Length != 6)
        {
            return null;
        }

        if (digits[0] == '0')
        {
            return null;
        }

        return int.Parse(digits, CultureInfo.InvariantCulture);
    }

    internal static string SerializeCreatePayload(ShiprocketCreatePayload payload) =>
        JsonSerializer.Serialize(payload, JsonOptions);

    /// <summary>Probe used by unit tests / preflight: serialize int PIN as JSON number.</summary>
    internal static string SerializeBillingPincodeProbe(int pincode) =>
        SerializeCreatePayload(new ShiprocketCreatePayload { BillingPincode = pincode, ShippingIsBilling = true });

    internal static string? ExtractBillingPincodeJsonToken(string requestJson)
    {
        using var doc = JsonDocument.Parse(requestJson);
        if (!doc.RootElement.TryGetProperty("billing_pincode", out var el))
        {
            return null;
        }

        return el.GetRawText();
    }

    internal static bool IsSixDigitJsonNumberToken(string? token) =>
        !string.IsNullOrWhiteSpace(token) &&
        token.Length == 6 &&
        token.All(char.IsDigit) &&
        token[0] != '0';

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

    private sealed record PickupGroup(string Pickup, List<OrderItem> Items);

    private sealed record ShiprocketCreateResult(string OrderId, string? ShipmentId, string? Status);

    internal sealed class ShiprocketCreatePayload
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

        /// <summary>Must serialize as a JSON number (e.g. 110001), never a quoted string.</summary>
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

        [JsonPropertyName("shipping_pincode")]
        public int? ShippingPincode { get; set; }

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

    internal sealed class ShiprocketOrderItemPayload
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
