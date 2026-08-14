using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bagly.Api.Data;
using Bagly.Api.Models;
using Bagly.Api.Options;
using Bagly.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Shipping;

public interface IAdminShippingService
{
    Task<AdminShippingOrdersResult> ListOrdersAsync(string? tab, CancellationToken cancellationToken = default);

    Task<ReadyToShipResponse> ReadyToShipAsync(Guid shipmentId, CancellationToken cancellationToken = default);

    Task<AssignAwbResponse> AssignAwbAsync(
        Guid shipmentId,
        int courierId,
        decimal? rate,
        CancellationToken cancellationToken = default);

    Task<GenerateLabelResponse> GenerateLabelAsync(
        Guid shipmentId,
        CancellationToken cancellationToken = default);

    Task<RequestPickupResponse> RequestPickupAsync(
        Guid shipmentId,
        CancellationToken cancellationToken = default);

    Task<GenerateManifestResponse> GenerateManifestAsync(
        Guid shipmentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShiprocketApiLogDto>> ListApiLogsAsync(
        Guid? orderId,
        Guid? shipmentId,
        string? orderNumber = null,
        int take = 50,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShipmentStatusLogDto>> ListStatusLogsAsync(
        Guid shipmentId,
        int take = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShiprocketWebhookLogDto>> ListWebhookLogsAsync(
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a tracking status change (webhook / system). Returns true when status changed.
    /// </summary>
    Task<bool> ApplyTrackingStatusAsync(
        Guid shipmentId,
        string trackingStatus,
        string source,
        string? rawJson = null,
        CancellationToken cancellationToken = default);
}

public sealed class AdminShippingService(
    IHttpClientFactory httpClientFactory,
    BaglyDbContext db,
    ShiprocketTokenStore tokenStore,
    IOptions<ShiprocketOptions> options,
    IShiprocketApiLogService apiLogs,
    IShiprocketWebhookLogService webhookLogs,
    ILogger<AdminShippingService> logger) : IAdminShippingService
{
    public const string TabNew = "new";
    /// <summary>Seller marked ready; admin can run serviceability (Ready to Ship).</summary>
    public const string TabReady = "ready";
    /// <summary>Admin ReadyToShipAt set; couriers / Assign AWB.</summary>
    public const string TabAssignAwb = "assign-awb";
    /// <summary>AWB assigned, waiting for label generation.</summary>
    public const string TabLabel = "label";
    /// <summary>Label URL stored; courier pickup not yet requested.</summary>
    public const string TabPickup = "pickup";
    /// <summary>Pickup requested; manifest not yet generated.</summary>
    public const string TabManifest = "manifest";
    /// <summary>Manifest generated (tracking may advance later).</summary>
    public const string TabInProgress = "in-progress";
    /// <summary>Back-compat alias for <see cref="TabPickup"/>.</summary>
    public const string TabLabeled = "labeled";

    public const string StatusReadyToShip = "ReadyToShip";
    public const string StatusAwbAssigned = "AwbAssigned";
    public const string StatusLabelGenerated = "LabelGenerated";
    public const string StatusPickupRequested = "PickupRequested";
    public const string StatusManifestGenerated = "ManifestGenerated";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ShiprocketOptions _options = options.Value;

    // Nickname → postcode cache (process lifetime; pickups rarely change).
    private static readonly object PickupCacheLock = new();
    private static Dictionary<string, int>? _pickupPostcodes;
    private static DateTimeOffset _pickupCacheExpires = DateTimeOffset.MinValue;

    public async Task<AdminShippingOrdersResult> ListOrdersAsync(
        string? tab,
        CancellationToken cancellationToken = default)
    {
        var normalizedTab = NormalizeTab(tab);

        var baseQuery = db.Orders.AsNoTracking()
            .Where(o => o.Status == "Confirmed")
            .Where(o => o.ShiprocketShipments.Any(s =>
                s.ShiprocketShipmentId != null && s.ShiprocketShipmentId != "" &&
                (s.Status == null || s.Status != "Cancelled") &&
                (s.ShippingStatus == null || s.ShippingStatus != "Cancelled")));

        // New: Shiprocket created, waiting on seller (not seller-ready, not admin-ready).
        var newCount = await baseQuery.CountAsync(
            o => o.ShiprocketShipments.Any(s =>
                s.ShiprocketShipmentId != null &&
                s.ShiprocketShipmentId != "" &&
                (s.AwbCode == null || s.AwbCode == "") &&
                s.ReadyToShipAt == null &&
                s.SellerReadyToShipAt == null &&
                (s.Status == null || s.Status != "Cancelled") &&
                (s.ShippingStatus == null || s.ShippingStatus != "Cancelled")),
            cancellationToken);

        // Ready to Ship: seller ready; admin has not run serviceability yet.
        var readyCount = await baseQuery.CountAsync(
            o => o.ShiprocketShipments.Any(s =>
                s.SellerReadyToShipAt != null &&
                s.ReadyToShipAt == null &&
                (s.AwbCode == null || s.AwbCode == "") &&
                (s.Status == null || s.Status != "Cancelled") &&
                (s.ShippingStatus == null || s.ShippingStatus != "Cancelled")),
            cancellationToken);

        // Assign AWB: admin ReadyToShipAt set, AWB not yet assigned.
        var assignAwbCount = await baseQuery.CountAsync(
            o => o.ShiprocketShipments.Any(s =>
                s.ReadyToShipAt != null &&
                (s.AwbCode == null || s.AwbCode == "") &&
                (s.Status == null || s.Status != "Cancelled") &&
                (s.ShippingStatus == null || s.ShippingStatus != "Cancelled")),
            cancellationToken);

        // Generate Label: AWB set, label URL not yet stored.
        var labelCount = await baseQuery.CountAsync(
            o => o.ShiprocketShipments.Any(s =>
                s.AwbCode != null && s.AwbCode != "" &&
                (s.LabelUrl == null || s.LabelUrl == "") &&
                (s.Status == null || s.Status != "Cancelled") &&
                (s.ShippingStatus == null || s.ShippingStatus != "Cancelled")),
            cancellationToken);

        // Request Pickup: label ready, pickup not yet requested.
        var pickupCount = await baseQuery.CountAsync(
            o => o.ShiprocketShipments.Any(s =>
                s.LabelUrl != null && s.LabelUrl != "" &&
                s.PickupRequestedAt == null &&
                (s.Status == null || s.Status != "Cancelled") &&
                (s.ShippingStatus == null || s.ShippingStatus != "Cancelled")),
            cancellationToken);

        // Generate Manifest: pickup requested, manifest URL not yet stored.
        var manifestCount = await baseQuery.CountAsync(
            o => o.ShiprocketShipments.Any(s =>
                s.PickupRequestedAt != null &&
                (s.ManifestUrl == null || s.ManifestUrl == "") &&
                (s.Status == null || s.Status != "Cancelled") &&
                (s.ShippingStatus == null || s.ShippingStatus != "Cancelled")),
            cancellationToken);

        // In Progress: manifest generated (tracking may advance later).
        var inProgressCount = await baseQuery.CountAsync(
            o => o.ShiprocketShipments.Any(s =>
                s.ManifestUrl != null && s.ManifestUrl != "" &&
                (s.Status == null || s.Status != "Cancelled") &&
                (s.ShippingStatus == null || s.ShippingStatus != "Cancelled")),
            cancellationToken);

        var filtered = normalizedTab switch
        {
            TabReady => baseQuery.Where(o => o.ShiprocketShipments.Any(s =>
                s.SellerReadyToShipAt != null &&
                s.ReadyToShipAt == null &&
                (s.AwbCode == null || s.AwbCode == "") &&
                (s.Status == null || s.Status != "Cancelled") &&
                (s.ShippingStatus == null || s.ShippingStatus != "Cancelled"))),
            TabAssignAwb => baseQuery.Where(o => o.ShiprocketShipments.Any(s =>
                s.ReadyToShipAt != null &&
                (s.AwbCode == null || s.AwbCode == "") &&
                (s.Status == null || s.Status != "Cancelled") &&
                (s.ShippingStatus == null || s.ShippingStatus != "Cancelled"))),
            TabLabel => baseQuery.Where(o => o.ShiprocketShipments.Any(s =>
                s.AwbCode != null && s.AwbCode != "" &&
                (s.LabelUrl == null || s.LabelUrl == "") &&
                (s.Status == null || s.Status != "Cancelled") &&
                (s.ShippingStatus == null || s.ShippingStatus != "Cancelled"))),
            TabPickup or TabLabeled => baseQuery.Where(o => o.ShiprocketShipments.Any(s =>
                s.LabelUrl != null && s.LabelUrl != "" &&
                s.PickupRequestedAt == null &&
                (s.Status == null || s.Status != "Cancelled") &&
                (s.ShippingStatus == null || s.ShippingStatus != "Cancelled"))),
            TabManifest => baseQuery.Where(o => o.ShiprocketShipments.Any(s =>
                s.PickupRequestedAt != null &&
                (s.ManifestUrl == null || s.ManifestUrl == "") &&
                (s.Status == null || s.Status != "Cancelled") &&
                (s.ShippingStatus == null || s.ShippingStatus != "Cancelled"))),
            TabInProgress => baseQuery.Where(o => o.ShiprocketShipments.Any(s =>
                s.ManifestUrl != null && s.ManifestUrl != "" &&
                (s.Status == null || s.Status != "Cancelled") &&
                (s.ShippingStatus == null || s.ShippingStatus != "Cancelled"))),
            _ => baseQuery.Where(o => o.ShiprocketShipments.Any(s =>
                s.ShiprocketShipmentId != null &&
                s.ShiprocketShipmentId != "" &&
                (s.AwbCode == null || s.AwbCode == "") &&
                s.ReadyToShipAt == null &&
                s.SellerReadyToShipAt == null &&
                (s.Status == null || s.Status != "Cancelled") &&
                (s.ShippingStatus == null || s.ShippingStatus != "Cancelled"))),
        };

        var orders = await filtered
            .OrderByDescending(o => o.CreatedAt)
            .Take(100)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                CustomerName = (o.FirstName + " " + o.LastName).Trim(),
                o.Email,
                o.Status,
                o.PaymentStatus,
                o.PaymentProvider,
                o.Currency,
                o.Total,
                o.Shipping,
                o.Zip,
                o.Phone,
                o.CreatedAt,
                Shipments = o.ShiprocketShipments
                    .OrderBy(s => s.PickupLocation)
                    .Select(s => new AdminShippingShipmentDto(
                        s.Id,
                        s.PickupLocation,
                        s.ShiprocketOrderId,
                        s.ShiprocketShipmentId,
                        s.Status,
                        s.ShippingStatus,
                        s.LastError,
                        s.AwbCode,
                        s.CourierId,
                        s.CourierName,
                        s.ActualShippingCharge,
                        s.ReadyToShipAt,
                        s.AwbAssignedAt,
                        s.CreatedAt,
                        s.UpdatedAt,
                        s.SellerReadyToShipAt,
                        s.SellerReadyToShipAt != null,
                        s.LabelUrl,
                        s.LabelGeneratedAt,
                        s.PickupRequestedAt,
                        s.PickupTokenNumber,
                        s.TrackingStatus,
                        s.TrackingStatusUpdatedAt,
                        s.ManifestUrl,
                        s.ManifestGeneratedAt))
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        var items = orders.Select(o => new AdminShippingOrderDto(
            o.Id,
            o.OrderNumber,
            o.CustomerName,
            o.Email,
            o.Status,
            o.PaymentStatus,
            o.PaymentProvider,
            o.Currency ?? "INR",
            o.Total,
            o.Shipping,
            o.Zip,
            o.Phone,
            o.CreatedAt,
            o.Shipments)).ToList();

        return new AdminShippingOrdersResult(
            items,
            items.Count,
            normalizedTab,
            newCount,
            readyCount,
            assignAwbCount,
            labelCount,
            LabeledCount: pickupCount,
            PickupCount: pickupCount,
            ManifestCount: manifestCount,
            InProgressCount: inProgressCount);
    }

    public async Task<IReadOnlyList<ShiprocketApiLogDto>> ListApiLogsAsync(
        Guid? orderId,
        Guid? shipmentId,
        string? orderNumber = null,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (orderId is null && !string.IsNullOrWhiteSpace(orderNumber))
        {
            var number = orderNumber.Trim();
            orderId = await db.Orders.AsNoTracking()
                .Where(o => o.OrderNumber == number)
                .Select(o => (Guid?)o.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (orderId is null)
            {
                return Array.Empty<ShiprocketApiLogDto>();
            }
        }

        return await apiLogs.ListAsync(orderId, shipmentId, take, cancellationToken);
    }

    public async Task<IReadOnlyList<ShipmentStatusLogDto>> ListStatusLogsAsync(
        Guid shipmentId,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(take, 1, 500);
        return await db.ShipmentStatusLogs.AsNoTracking()
            .Where(l => l.OrderShiprocketShipmentId == shipmentId)
            .OrderByDescending(l => l.CreatedAtUtc)
            .ThenByDescending(l => l.Id)
            .Take(limit)
            .Select(l => new ShipmentStatusLogDto(
                l.Id,
                l.OrderId,
                l.OrderShiprocketShipmentId,
                l.AwbCode,
                l.ShiprocketShipmentId,
                l.FromStatus,
                l.ToStatus,
                l.Source,
                l.Message,
                l.RawJson,
                l.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShiprocketWebhookLogDto>> ListWebhookLogsAsync(
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        return await webhookLogs.ListAsync(take, cancellationToken);
    }

    public async Task<ReadyToShipResponse> ReadyToShipAsync(
        Guid shipmentId,
        CancellationToken cancellationToken = default)
    {
        EnsureShiprocketConfigured();

        var shipment = await db.OrderShiprocketShipments
            .Include(s => s.Order!)
            .ThenInclude(o => o.Items)
            .FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken)
            ?? throw new InvalidOperationException("Shipment not found.");

        var order = shipment.Order
            ?? throw new InvalidOperationException("Order not found for shipment.");

        if (string.IsNullOrWhiteSpace(shipment.ShiprocketShipmentId))
        {
            throw new InvalidOperationException(
                "Shiprocket shipment_id is missing. Create the Shiprocket order first (Orders → Retry Shiprocket).");
        }

        if (!string.IsNullOrWhiteSpace(shipment.AwbCode))
        {
            throw new InvalidOperationException($"AWB already assigned ({shipment.AwbCode}).");
        }

        if (shipment.SellerReadyToShipAt is null)
        {
            throw new InvalidOperationException(
                "Waiting for seller to mark this shipment Ready to Ship.");
        }

        if (string.Equals(shipment.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(shipment.ShippingStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This shipment was cancelled.");
        }

        var deliveryPostcode = ShiprocketService.NormalizePincode(order.Zip)
            ?? throw new InvalidOperationException($"Order zip '{order.Zip}' is not a valid 6-digit PIN.");

        var pickupPostcode = await ResolvePickupPostcodeAsync(
            shipment.PickupLocation, order.Id, shipment.Id, cancellationToken);
        var isCod = string.Equals(order.PaymentProvider, "COD", StringComparison.OrdinalIgnoreCase);
        var package = await ResolvePackageForShipmentAsync(order, shipment.PickupLocation, cancellationToken);
        var weightKg = package.WeightKg;
        var length = package.Length;
        var breadth = package.Breadth;
        var height = package.Height;
        var declaredValue = await ResolveDeclaredValueAsync(order, shipment.PickupLocation, cancellationToken);

        var couriers = await GetServiceabilityAsync(
            pickupPostcode,
            deliveryPostcode,
            weightKg,
            length,
            breadth,
            height,
            isCod,
            declaredValue,
            order.Id,
            shipment.Id,
            cancellationToken);

        shipment.ShippingStatus = StatusReadyToShip;
        shipment.ReadyToShipAt ??= DateTime.UtcNow;
        shipment.UpdatedAt = DateTime.UtcNow;
        shipment.LastError = null;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Ready to ship for {OrderNumber}/{Pickup}: shipmentId={ShiprocketShipmentId}, pickupPin={PickupPin}, deliveryPin={DeliveryPin}, weight={Weight}, dims={L}x{B}x{H}, declared={Declared}, couriers={CourierCount}.",
            order.OrderNumber,
            shipment.PickupLocation,
            shipment.ShiprocketShipmentId,
            pickupPostcode,
            deliveryPostcode,
            weightKg,
            length,
            breadth,
            height,
            declaredValue,
            couriers.Count);

        return new ReadyToShipResponse(
            shipment.Id,
            order.Id,
            shipment.PickupLocation,
            shipment.ShiprocketShipmentId,
            pickupPostcode,
            deliveryPostcode,
            isCod,
            weightKg,
            length,
            breadth,
            height,
            declaredValue,
            shipment.ShippingStatus!,
            shipment.ReadyToShipAt,
            couriers);
    }

    public async Task<AssignAwbResponse> AssignAwbAsync(
        Guid shipmentId,
        int courierId,
        decimal? rate,
        CancellationToken cancellationToken = default)
    {
        EnsureShiprocketConfigured();

        if (courierId <= 0)
        {
            throw new InvalidOperationException("courierId is required.");
        }

        var shipment = await db.OrderShiprocketShipments
            .Include(s => s.Order)
            .FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken)
            ?? throw new InvalidOperationException("Shipment not found.");

        var order = shipment.Order
            ?? throw new InvalidOperationException("Order not found for shipment.");

        if (string.IsNullOrWhiteSpace(shipment.ShiprocketShipmentId))
        {
            throw new InvalidOperationException("Shiprocket shipment_id is missing.");
        }

        if (!string.IsNullOrWhiteSpace(shipment.AwbCode))
        {
            throw new InvalidOperationException($"AWB already assigned ({shipment.AwbCode}).");
        }

        if (!long.TryParse(shipment.ShiprocketShipmentId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var srShipmentId))
        {
            throw new InvalidOperationException(
                $"Shiprocket shipment_id '{shipment.ShiprocketShipmentId}' is not numeric.");
        }

        var assignResult = await AssignAwbWithAuthRetryAsync(
            srShipmentId, courierId, order.Id, shipment.Id, cancellationToken);

        shipment.AwbCode = assignResult.AwbCode;
        shipment.CourierId = assignResult.CourierId ?? courierId;
        shipment.CourierName = assignResult.CourierName;
        shipment.ActualShippingCharge = rate is decimal r
            ? RoundMoney(r)
            : assignResult.FreightCharge is decimal f
                ? RoundMoney(f)
                : null;
        shipment.ShippingStatus = StatusAwbAssigned;
        shipment.AwbAssignedAt = DateTime.UtcNow;
        shipment.ReadyToShipAt ??= DateTime.UtcNow;
        shipment.Status = StatusAwbAssigned;
        shipment.UpdatedAt = DateTime.UtcNow;
        shipment.LastError = null;

        // Mirror primary AWB onto order when this is the first/primary shipment row.
        if (string.IsNullOrWhiteSpace(order.ShiprocketShipmentId) ||
            string.Equals(order.ShiprocketShipmentId, shipment.ShiprocketShipmentId, StringComparison.Ordinal))
        {
            order.ShiprocketStatus = StatusAwbAssigned;
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "AWB assigned for {OrderNumber}/{Pickup}: awb={Awb}, courier={CourierId}/{CourierName}, charge={Charge}.",
            order.OrderNumber,
            shipment.PickupLocation,
            shipment.AwbCode,
            shipment.CourierId,
            shipment.CourierName,
            shipment.ActualShippingCharge);

        return new AssignAwbResponse(
            shipment.Id,
            order.Id,
            shipment.PickupLocation,
            shipment.AwbCode,
            shipment.CourierId,
            shipment.CourierName,
            shipment.ActualShippingCharge,
            shipment.ShippingStatus!,
            shipment.AwbAssignedAt);
    }

    /// <summary>
    /// Calls Shiprocket <c>POST v1/external/courier/generate/label</c> with
    /// <c>{ "shipment_id": [srShipmentId] }</c>, stores <see cref="OrderShiprocketShipment.LabelUrl"/>.
    /// </summary>
    public async Task<GenerateLabelResponse> GenerateLabelAsync(
        Guid shipmentId,
        CancellationToken cancellationToken = default)
    {
        EnsureShiprocketConfigured();

        var shipment = await db.OrderShiprocketShipments
            .Include(s => s.Order)
            .FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken)
            ?? throw new InvalidOperationException("Shipment not found.");

        var order = shipment.Order
            ?? throw new InvalidOperationException("Order not found for shipment.");

        if (string.IsNullOrWhiteSpace(shipment.ShiprocketShipmentId))
        {
            throw new InvalidOperationException("Shiprocket shipment_id is missing.");
        }

        if (string.IsNullOrWhiteSpace(shipment.AwbCode))
        {
            throw new InvalidOperationException("Assign AWB before generating a label.");
        }

        if (!string.IsNullOrWhiteSpace(shipment.LabelUrl))
        {
            throw new InvalidOperationException("Label already generated for this shipment.");
        }

        if (!long.TryParse(shipment.ShiprocketShipmentId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var srShipmentId))
        {
            throw new InvalidOperationException(
                $"Shiprocket shipment_id '{shipment.ShiprocketShipmentId}' is not numeric.");
        }

        var labelUrl = await GenerateLabelWithAuthRetryAsync(
            srShipmentId, order.Id, shipment.Id, cancellationToken);

        shipment.LabelUrl = labelUrl;
        shipment.LabelGeneratedAt = DateTime.UtcNow;
        shipment.ShippingStatus = StatusLabelGenerated;
        shipment.Status = StatusLabelGenerated;
        shipment.UpdatedAt = DateTime.UtcNow;
        shipment.LastError = null;

        if (string.IsNullOrWhiteSpace(order.ShiprocketShipmentId) ||
            string.Equals(order.ShiprocketShipmentId, shipment.ShiprocketShipmentId, StringComparison.Ordinal))
        {
            order.ShiprocketStatus = StatusLabelGenerated;
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Label generated for {OrderNumber}/{Pickup}: awb={Awb}, url={Url}.",
            order.OrderNumber,
            shipment.PickupLocation,
            shipment.AwbCode,
            Truncate(shipment.LabelUrl, 120));

        return new GenerateLabelResponse(
            shipment.Id,
            order.Id,
            shipment.PickupLocation,
            shipment.AwbCode,
            shipment.LabelUrl,
            shipment.ShippingStatus!,
            shipment.LabelGeneratedAt);
    }

    /// <summary>
    /// Calls Shiprocket <c>POST v1/external/courier/generate/pickup</c> with
    /// <c>{ "shipment_id": [srShipmentId] }</c>, stores pickup token / timestamps,
    /// and sets tracking to <see cref="ShipmentTrackingStatus.PickupRequested"/>.
    /// </summary>
    public async Task<RequestPickupResponse> RequestPickupAsync(
        Guid shipmentId,
        CancellationToken cancellationToken = default)
    {
        EnsureShiprocketConfigured();

        var shipment = await db.OrderShiprocketShipments
            .Include(s => s.Order)
            .FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken)
            ?? throw new InvalidOperationException("Shipment not found.");

        var order = shipment.Order
            ?? throw new InvalidOperationException("Order not found for shipment.");

        if (string.IsNullOrWhiteSpace(shipment.ShiprocketShipmentId))
        {
            throw new InvalidOperationException("Shiprocket shipment_id is missing.");
        }

        if (string.IsNullOrWhiteSpace(shipment.AwbCode))
        {
            throw new InvalidOperationException("Assign AWB before requesting pickup.");
        }

        if (string.IsNullOrWhiteSpace(shipment.LabelUrl))
        {
            throw new InvalidOperationException("Generate label before requesting pickup.");
        }

        if (shipment.PickupRequestedAt is not null)
        {
            throw new InvalidOperationException("Pickup already requested for this shipment.");
        }

        if (!long.TryParse(shipment.ShiprocketShipmentId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var srShipmentId))
        {
            throw new InvalidOperationException(
                $"Shiprocket shipment_id '{shipment.ShiprocketShipmentId}' is not numeric.");
        }

        var pickupResult = await GeneratePickupWithAuthRetryAsync(
            srShipmentId, order.Id, shipment.Id, cancellationToken);

        var now = DateTime.UtcNow;
        var fromStatus = shipment.TrackingStatus;
        shipment.PickupRequestedAt = now;
        if (!string.IsNullOrWhiteSpace(pickupResult.PickupTokenNumber))
        {
            shipment.PickupTokenNumber = pickupResult.PickupTokenNumber.Trim();
        }

        shipment.ShippingStatus = StatusPickupRequested;
        shipment.Status = StatusPickupRequested;
        shipment.TrackingStatus = ShipmentTrackingStatus.PickupRequested;
        shipment.TrackingStatusUpdatedAt = now;
        shipment.UpdatedAt = now;
        shipment.LastError = null;

        if (string.IsNullOrWhiteSpace(order.ShiprocketShipmentId) ||
            string.Equals(order.ShiprocketShipmentId, shipment.ShiprocketShipmentId, StringComparison.Ordinal))
        {
            order.ShiprocketStatus = StatusPickupRequested;
        }

        var rawJson = Truncate(pickupResult.RawJson, 3900);
        db.OrderShipmentTrackings.Add(new OrderShipmentTracking
        {
            OrderId = order.Id,
            OrderShiprocketShipmentId = shipment.Id,
            ShiprocketShipmentId = shipment.ShiprocketShipmentId,
            AwbCode = shipment.AwbCode,
            Status = ShipmentTrackingStatus.PickupRequested,
            ChangedAtUtc = now,
            Source = ShipmentTrackingStatus.SourceAdmin,
            RawJson = rawJson,
        });

        db.ShipmentStatusLogs.Add(new ShipmentStatusLog
        {
            OrderId = order.Id,
            OrderShiprocketShipmentId = shipment.Id,
            AwbCode = shipment.AwbCode,
            ShiprocketShipmentId = shipment.ShiprocketShipmentId,
            FromStatus = fromStatus,
            ToStatus = ShipmentTrackingStatus.PickupRequested,
            Source = ShipmentTrackingStatus.SourceAdmin,
            Message = "Pickup requested via admin shipping.",
            RawJson = rawJson,
            CreatedAtUtc = now,
        });

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Pickup requested for {OrderNumber}/{Pickup}: awb={Awb}, token={Token}.",
            order.OrderNumber,
            shipment.PickupLocation,
            shipment.AwbCode,
            shipment.PickupTokenNumber ?? "(none)");

        return new RequestPickupResponse(
            shipment.Id,
            order.Id,
            shipment.PickupLocation,
            shipment.AwbCode,
            shipment.PickupTokenNumber,
            shipment.ShippingStatus!,
            shipment.TrackingStatus,
            shipment.PickupRequestedAt);
    }

    /// <summary>
    /// Calls Shiprocket <c>POST v1/external/manifests/generate</c> with
    /// <c>{ "shipment_id": [srShipmentId] }</c>, stores <see cref="OrderShiprocketShipment.ManifestUrl"/>.
    /// </summary>
    public async Task<GenerateManifestResponse> GenerateManifestAsync(
        Guid shipmentId,
        CancellationToken cancellationToken = default)
    {
        EnsureShiprocketConfigured();

        var shipment = await db.OrderShiprocketShipments
            .Include(s => s.Order)
            .FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken)
            ?? throw new InvalidOperationException("Shipment not found.");

        var order = shipment.Order
            ?? throw new InvalidOperationException("Order not found for shipment.");

        if (string.IsNullOrWhiteSpace(shipment.ShiprocketShipmentId))
        {
            throw new InvalidOperationException("Shiprocket shipment_id is missing.");
        }

        if (string.IsNullOrWhiteSpace(shipment.AwbCode))
        {
            throw new InvalidOperationException("Assign AWB before generating a manifest.");
        }

        if (shipment.PickupRequestedAt is null)
        {
            throw new InvalidOperationException("Request pickup before generating a manifest.");
        }

        if (!string.IsNullOrWhiteSpace(shipment.ManifestUrl))
        {
            throw new InvalidOperationException("Manifest already generated for this shipment.");
        }

        if (!long.TryParse(shipment.ShiprocketShipmentId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var srShipmentId))
        {
            throw new InvalidOperationException(
                $"Shiprocket shipment_id '{shipment.ShiprocketShipmentId}' is not numeric.");
        }

        var manifestUrl = await GenerateManifestWithAuthRetryAsync(
            srShipmentId, order.Id, shipment.Id, cancellationToken);

        var now = DateTime.UtcNow;
        shipment.ManifestUrl = manifestUrl;
        shipment.ManifestGeneratedAt = now;
        shipment.ShippingStatus = StatusManifestGenerated;
        shipment.Status = StatusManifestGenerated;
        shipment.UpdatedAt = now;
        shipment.LastError = null;

        if (string.IsNullOrWhiteSpace(order.ShiprocketShipmentId) ||
            string.Equals(order.ShiprocketShipmentId, shipment.ShiprocketShipmentId, StringComparison.Ordinal))
        {
            order.ShiprocketStatus = StatusManifestGenerated;
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Manifest generated for {OrderNumber}/{Pickup}: awb={Awb}, url={Url}.",
            order.OrderNumber,
            shipment.PickupLocation,
            shipment.AwbCode,
            Truncate(shipment.ManifestUrl, 120));

        return new GenerateManifestResponse(
            shipment.Id,
            order.Id,
            shipment.PickupLocation,
            shipment.AwbCode,
            shipment.ManifestUrl,
            shipment.ShippingStatus!,
            shipment.ManifestGeneratedAt);
    }

    public async Task<bool> ApplyTrackingStatusAsync(
        Guid shipmentId,
        string trackingStatus,
        string source,
        string? rawJson = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackingStatus))
        {
            return false;
        }

        var normalized = trackingStatus.Trim().ToUpperInvariant();
        if (ShipmentTrackingStatus.Rank(normalized) <= 0)
        {
            return false;
        }

        var shipment = await db.OrderShiprocketShipments
            .Include(s => s.Order)
            .FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return false;
        }

        if (string.Equals(shipment.TrackingStatus, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Never move backwards (e.g. DELIVERED → IN_TRANSIT) unless admin/system forces later.
        if (!ShipmentTrackingStatus.IsForwardOf(normalized, shipment.TrackingStatus))
        {
            logger.LogInformation(
                "Ignoring non-forward tracking update for shipment {ShipmentId}: {From} → {To} (source={Source}).",
                shipmentId,
                shipment.TrackingStatus,
                normalized,
                source);
            return false;
        }

        var now = DateTime.UtcNow;
        var fromStatus = shipment.TrackingStatus;
        var resolvedSource = string.IsNullOrWhiteSpace(source)
            ? ShipmentTrackingStatus.SourceSystem
            : source.Trim();
        var truncatedRaw = Truncate(rawJson, 3900);

        shipment.TrackingStatus = normalized;
        shipment.TrackingStatusUpdatedAt = now;
        shipment.UpdatedAt = now;

        if (shipment.PickupRequestedAt is null &&
            string.Equals(normalized, ShipmentTrackingStatus.PickupRequested, StringComparison.OrdinalIgnoreCase))
        {
            shipment.PickupRequestedAt = now;
            shipment.ShippingStatus = StatusPickupRequested;
            shipment.Status = StatusPickupRequested;
        }

        db.OrderShipmentTrackings.Add(new OrderShipmentTracking
        {
            OrderId = shipment.OrderId,
            OrderShiprocketShipmentId = shipment.Id,
            ShiprocketShipmentId = shipment.ShiprocketShipmentId,
            AwbCode = shipment.AwbCode,
            Status = normalized,
            ChangedAtUtc = now,
            Source = resolvedSource,
            RawJson = truncatedRaw,
        });

        db.ShipmentStatusLogs.Add(new ShipmentStatusLog
        {
            OrderId = shipment.OrderId,
            OrderShiprocketShipmentId = shipment.Id,
            AwbCode = shipment.AwbCode,
            ShiprocketShipmentId = shipment.ShiprocketShipmentId,
            FromStatus = fromStatus,
            ToStatus = normalized,
            Source = resolvedSource,
            Message = string.IsNullOrWhiteSpace(fromStatus)
                ? $"Status set to {normalized}."
                : $"Status changed {fromStatus} → {normalized}.",
            RawJson = truncatedRaw,
            CreatedAtUtc = now,
        });

        if (shipment.Order is not null &&
            (string.IsNullOrWhiteSpace(shipment.Order.ShiprocketShipmentId) ||
             string.Equals(shipment.Order.ShiprocketShipmentId, shipment.ShiprocketShipmentId, StringComparison.Ordinal)))
        {
            shipment.Order.ShiprocketStatus = normalized;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void EnsureShiprocketConfigured()
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("Shiprocket__Enabled is false.");
        }

        if (ShiprocketOptions.IsMissingCredential(_options.Email) ||
            ShiprocketOptions.IsMissingCredential(_options.Password))
        {
            throw new InvalidOperationException("Shiprocket Email/Password missing or still SET_VIA_ENV placeholders.");
        }
    }

    private static string NormalizeTab(string? tab)
    {
        if (string.Equals(tab, TabReady, StringComparison.OrdinalIgnoreCase)) return TabReady;
        if (string.Equals(tab, TabAssignAwb, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tab, "assign", StringComparison.OrdinalIgnoreCase))
        {
            return TabAssignAwb;
        }

        if (string.Equals(tab, TabLabel, StringComparison.OrdinalIgnoreCase)) return TabLabel;
        // Back-compat: old "awb" tab → Generate Label (AWB without label).
        if (string.Equals(tab, "awb", StringComparison.OrdinalIgnoreCase)) return TabLabel;
        if (string.Equals(tab, TabPickup, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tab, TabLabeled, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tab, "label-generated", StringComparison.OrdinalIgnoreCase))
        {
            return TabPickup;
        }

        if (string.Equals(tab, TabManifest, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tab, "generate-manifest", StringComparison.OrdinalIgnoreCase))
        {
            return TabManifest;
        }

        if (string.Equals(tab, TabInProgress, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tab, "progress", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tab, "picked-up", StringComparison.OrdinalIgnoreCase))
        {
            return TabInProgress;
        }

        return TabNew;
    }

    private async Task<int> ResolvePickupPostcodeAsync(
        string pickupNickname,
        Guid? orderId,
        Guid? shipmentId,
        CancellationToken cancellationToken)
    {
        var map = await GetPickupPostcodeMapAsync(orderId, shipmentId, cancellationToken);
        if (map.TryGetValue(pickupNickname.Trim(), out var pin))
        {
            return pin;
        }

        // Case-insensitive fallback (nickname matching for create is case-sensitive, but postcodes are numeric).
        var ci = map.FirstOrDefault(kv =>
            string.Equals(kv.Key, pickupNickname.Trim(), StringComparison.OrdinalIgnoreCase));
        if (ci.Key is not null)
        {
            return ci.Value;
        }

        throw new InvalidOperationException(
            $"Could not resolve pickup postcode for nickname '{pickupNickname}'. " +
            "Ensure the pickup exists in Shiprocket → Settings → Pickup Addresses and has a pin code.");
    }

    private async Task<IReadOnlyDictionary<string, int>> GetPickupPostcodeMapAsync(
        Guid? orderId,
        Guid? shipmentId,
        CancellationToken cancellationToken)
    {
        lock (PickupCacheLock)
        {
            if (_pickupPostcodes is not null && DateTimeOffset.UtcNow < _pickupCacheExpires)
            {
                return _pickupPostcodes;
            }
        }

        var map = await FetchPickupPostcodesAsync(orderId, shipmentId, cancellationToken);
        lock (PickupCacheLock)
        {
            _pickupPostcodes = map;
            _pickupCacheExpires = DateTimeOffset.UtcNow.AddMinutes(30);
        }

        return map;
    }

    private async Task<Dictionary<string, int>> FetchPickupPostcodesAsync(
        Guid? orderId,
        Guid? shipmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FetchPickupPostcodesOnceAsync(forceLogin: false, orderId, shipmentId, cancellationToken);
        }
        catch (ShiprocketAuthException)
        {
            tokenStore.Invalidate();
            return await FetchPickupPostcodesOnceAsync(forceLogin: true, orderId, shipmentId, cancellationToken);
        }
    }

    private async Task<Dictionary<string, int>> FetchPickupPostcodesOnceAsync(
        bool forceLogin,
        Guid? orderId,
        Guid? shipmentId,
        CancellationToken cancellationToken)
    {
        var token = forceLogin
            ? await LoginAsync(cancellationToken)
            : tokenStore.GetValidToken() ?? await LoginAsync(cancellationToken);

        const string path = "v1/external/settings/company/pickup";
        var client = httpClientFactory.CreateClient("Shiprocket");
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        await apiLogs.LogAsync(
            "PickupList",
            "GET",
            path,
            requestJson: null,
            responseStatus: (int)response.StatusCode,
            responseJson: raw,
            orderId: orderId,
            shipmentId: shipmentId,
            cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenStore.Invalidate();
            throw new ShiprocketAuthException($"Shiprocket pickup list returned 401. Body: {Truncate(raw)}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Shiprocket pickup list failed HTTP {(int)response.StatusCode}. Body: {Truncate(raw, 480)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        CollectPickupPostcodes(doc.RootElement, map);
        return map;
    }

    private static void CollectPickupPostcodes(JsonElement el, Dictionary<string, int> map)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            string? nickname = null;
            int? pin = null;

            if (el.TryGetProperty("pickup_location", out var pl) && pl.ValueKind == JsonValueKind.String)
            {
                nickname = pl.GetString();
            }

            pin = TryReadPostcode(el, "pin_code")
                  ?? TryReadPostcode(el, "pincode")
                  ?? TryReadPostcode(el, "postal_code")
                  ?? TryReadPostcode(el, "zipcode");

            if (!string.IsNullOrWhiteSpace(nickname) && pin is int p)
            {
                map[nickname.Trim()] = p;
            }

            foreach (var prop in el.EnumerateObject())
            {
                CollectPickupPostcodes(prop.Value, map);
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                CollectPickupPostcodes(item, map);
            }
        }
    }

    private static int? TryReadPostcode(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el))
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
        {
            return ShiprocketService.NormalizePincode(n.ToString(CultureInfo.InvariantCulture));
        }

        if (el.ValueKind == JsonValueKind.String)
        {
            return ShiprocketService.NormalizePincode(el.GetString());
        }

        return null;
    }

    private async Task<ShiprocketPackageResolver.PackageSize> ResolvePackageForShipmentAsync(
        Order order,
        string pickupLocation,
        CancellationToken cancellationToken)
    {
        var defaultPickup = _options.PickupLocation.Trim();
        var productIds = order.Items
            .Select(i => i.ProductId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Dictionary<string, string?> productPickups;
        Dictionary<string, ShiprocketPackageResolver.ProductPackageInfo> productPackages;
        if (productIds.Count == 0)
        {
            productPickups = new Dictionary<string, string?>(StringComparer.Ordinal);
            productPackages = new Dictionary<string, ShiprocketPackageResolver.ProductPackageInfo>(StringComparer.Ordinal);
        }
        else
        {
            var rows = await db.Products.AsNoTracking()
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

            productPickups = rows.ToDictionary(
                p => p.Id,
                p => p.ShiprocketPickupLocation,
                StringComparer.Ordinal);
            productPackages = rows.ToDictionary(
                p => p.Id,
                p => new ShiprocketPackageResolver.ProductPackageInfo(
                    p.UseDefaultPackageSize,
                    p.WeightKg,
                    p.LengthCm,
                    p.BreadthCm,
                    p.HeightCm),
                StringComparer.Ordinal);
        }

        var groupLines = order.Items
            .Where(item =>
            {
                productPickups.TryGetValue(item.ProductId, out var productPickup);
                var pickup = string.IsNullOrWhiteSpace(productPickup) ? defaultPickup : productPickup.Trim();
                return string.Equals(pickup, pickupLocation, StringComparison.Ordinal);
            })
            .Select(item =>
            {
                productPackages.TryGetValue(item.ProductId, out var info);
                return (item.Quantity, (ShiprocketPackageResolver.ProductPackageInfo?)info);
            });

        return ShiprocketPackageResolver.ResolveForLines(groupLines, _options);
    }

    private async Task<decimal> ResolveDeclaredValueAsync(
        Order order,
        string pickupLocation,
        CancellationToken cancellationToken)
    {
        var defaultPickup = _options.PickupLocation.Trim();

        var productIds = order.Items
            .Select(i => i.ProductId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Dictionary<string, string?> productPickups;
        if (productIds.Count == 0)
        {
            productPickups = new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        else
        {
            productPickups = await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .Select(p => new { p.Id, p.ShiprocketPickupLocation })
                .ToDictionaryAsync(p => p.Id, p => p.ShiprocketPickupLocation, StringComparer.Ordinal, cancellationToken);
        }

        var lineSubtotal = order.Items
            .Where(item =>
            {
                productPickups.TryGetValue(item.ProductId, out var productPickup);
                var pickup = string.IsNullOrWhiteSpace(productPickup) ? defaultPickup : productPickup.Trim();
                return string.Equals(pickup, pickupLocation, StringComparison.Ordinal);
            })
            .Sum(i => i.UnitPrice * i.Quantity);

        if (lineSubtotal > 0)
        {
            return lineSubtotal;
        }

        // Fallback: order goods subtotal, then total (never invent a charge from shipping alone).
        if (order.Subtotal > 0) return order.Subtotal;
        return order.Total > 0 ? order.Total : 0m;
    }

    private async Task<IReadOnlyList<CourierOptionDto>> GetServiceabilityAsync(
        int pickupPostcode,
        int deliveryPostcode,
        double weightKg,
        double length,
        double breadth,
        double height,
        bool isCod,
        decimal declaredValue,
        Guid orderId,
        Guid shipmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetServiceabilityOnceAsync(
                pickupPostcode, deliveryPostcode, weightKg, length, breadth, height, isCod, declaredValue,
                forceLogin: false, orderId, shipmentId, cancellationToken);
        }
        catch (ShiprocketAuthException)
        {
            tokenStore.Invalidate();
            return await GetServiceabilityOnceAsync(
                pickupPostcode, deliveryPostcode, weightKg, length, breadth, height, isCod, declaredValue,
                forceLogin: true, orderId, shipmentId, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<CourierOptionDto>> GetServiceabilityOnceAsync(
        int pickupPostcode,
        int deliveryPostcode,
        double weightKg,
        double length,
        double breadth,
        double height,
        bool isCod,
        decimal declaredValue,
        bool forceLogin,
        Guid orderId,
        Guid shipmentId,
        CancellationToken cancellationToken)
    {
        var token = forceLogin
            ? await LoginAsync(cancellationToken)
            : tokenStore.GetValidToken() ?? await LoginAsync(cancellationToken);

        // Match Shiprocket panel: all dimensions + declared_value must be present.
        var qs = new QueryStringBuilder()
            .Add("pickup_postcode", pickupPostcode.ToString(CultureInfo.InvariantCulture))
            .Add("delivery_postcode", deliveryPostcode.ToString(CultureInfo.InvariantCulture))
            .Add("weight", weightKg.ToString("0.###", CultureInfo.InvariantCulture))
            .Add("length", FormatDim(length))
            .Add("breadth", FormatDim(breadth))
            .Add("height", FormatDim(height))
            .Add("cod", isCod ? "1" : "0")
            .Add("declared_value", ((int)Math.Round(Math.Max(0, declaredValue), MidpointRounding.AwayFromZero))
                .ToString(CultureInfo.InvariantCulture));

        var query = qs.ToString();
        var path = "v1/external/courier/serviceability/?" + query;
        var client = httpClientFactory.CreateClient("Shiprocket");
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        await apiLogs.LogAsync(
            "Serviceability",
            "GET",
            path,
            requestJson: query,
            responseStatus: (int)response.StatusCode,
            responseJson: raw,
            orderId: orderId,
            shipmentId: shipmentId,
            cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenStore.Invalidate();
            throw new ShiprocketAuthException($"Shiprocket serviceability returned 401. Body: {Truncate(raw)}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Shiprocket serviceability failed HTTP {(int)response.StatusCode}. Body: {Truncate(raw, 480)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var root = doc.RootElement;
        var list = FindCourierArray(root);
        if (list is null)
        {
            var msg = TryReadMessage(root) ?? Truncate(raw, 300);
            throw new InvalidOperationException($"Shiprocket serviceability returned no couriers. {msg}");
        }

        var couriers = new List<CourierOptionDto>();
        foreach (var item in list.Value.EnumerateArray())
        {
            var id = TryReadInt(item, "courier_company_id")
                     ?? TryReadInt(item, "courier_id")
                     ?? TryReadInt(item, "id");
            if (id is null or <= 0) continue;

            var name = TryReadString(item, "courier_name")
                       ?? TryReadString(item, "courier_company_name")
                       ?? $"Courier {id}";
            // Delivery ETA: prefer etd / estimated_delivery / edd; etd_hours is delivery hours (int).
            var etd = TryReadString(item, "etd")
                      ?? TryReadString(item, "estimated_delivery")
                      ?? TryReadString(item, "edd");
            if (string.IsNullOrWhiteSpace(etd))
            {
                var etdHours = TryReadInt(item, "etd_hours");
                if (etdHours is > 0)
                    etd = $"{etdHours} hours";
            }

            var days = TryReadInt(item, "estimated_delivery_days");
            var rating = TryReadDecimal(item, "rating");
            var expectedPickup = FormatExpectedPickup(item);

            var (rate, freight, coverage, whatsapp, codCharge) = ComputeCourierShippingCharge(item);

            couriers.Add(new CourierOptionDto(
                id.Value,
                name,
                rate,
                etd,
                days,
                freight,
                coverage,
                whatsapp,
                codCharge,
                rating,
                expectedPickup));
        }

        return couriers
            .OrderBy(c => c.Rate)
            .ThenBy(c => c.CourierName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Panel-aligned shipping charge: Freight + Coverage + WhatsApp [+ COD].
    /// Declared/order value is an API input only — never added into the rate.
    /// Always sum fee components as decimal (never truncate to int); round to 2 dp.
    /// </summary>
    private static (decimal Rate, decimal Freight, decimal Coverage, decimal WhatsApp, decimal Cod)
        ComputeCourierShippingCharge(JsonElement item)
    {
        var freight = TryReadDecimal(item, "freight_charge") ?? 0m;
        var coverage = TryReadDecimal(item, "coverage_charges")
                       ?? TryReadDecimal(item, "coverage_charge")
                       ?? TryReadDecimal(item, "insurance_charge")
                       ?? TryReadDecimal(item, "insurance_charges")
                       ?? 0m;
        var whatsapp = TryReadDecimal(item, "whatsapp_charge")
                       ?? TryReadDecimal(item, "whatsapp_charges")
                       ?? TryReadDecimal(item, "other_charges")
                       ?? 0m;
        var codCharge = TryReadDecimal(item, "cod_charges")
                        ?? TryReadDecimal(item, "cod_charge")
                        ?? 0m;

        // Keep component decimals (e.g. freight 138.36); only round the money total.
        var componentSum = RoundMoney(freight + coverage + whatsapp + codCharge);
        var panelRate = RoundMoney(
            TryReadDecimal(item, "rate")
            ?? TryReadDecimal(item, "total_charge")
            ?? TryReadDecimal(item, "total_charges")
            ?? 0m);

        // Prefer explicit fee sum so a truncated panel <c>rate</c> (e.g. 242) cannot
        // replace 138.36+99+5=242.36.
        if (componentSum > 0m)
        {
            return (componentSum, freight, coverage, whatsapp, codCharge);
        }

        return (panelRate, freight, coverage, whatsapp, codCharge);
    }

    private static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string FormatDim(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private async Task<AssignAwbApiResult> AssignAwbWithAuthRetryAsync(
        long shipmentId,
        int courierId,
        Guid orderId,
        Guid baglyShipmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await AssignAwbOnceAsync(
                shipmentId, courierId, forceLogin: false, orderId, baglyShipmentId, cancellationToken);
        }
        catch (ShiprocketAuthException)
        {
            tokenStore.Invalidate();
            return await AssignAwbOnceAsync(
                shipmentId, courierId, forceLogin: true, orderId, baglyShipmentId, cancellationToken);
        }
    }

    private async Task<AssignAwbApiResult> AssignAwbOnceAsync(
        long shipmentId,
        int courierId,
        bool forceLogin,
        Guid orderId,
        Guid baglyShipmentId,
        CancellationToken cancellationToken)
    {
        var token = forceLogin
            ? await LoginAsync(cancellationToken)
            : tokenStore.GetValidToken() ?? await LoginAsync(cancellationToken);

        var body = new Dictionary<string, object>
        {
            ["shipment_id"] = shipmentId,
            ["courier_id"] = courierId,
        };
        var requestJson = JsonSerializer.Serialize(body, JsonOptions);
        const string path = "v1/external/courier/assign/awb";

        var client = httpClientFactory.CreateClient("Shiprocket");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        await apiLogs.LogAsync(
            "AssignAwb",
            "POST",
            path,
            requestJson: requestJson,
            responseStatus: (int)response.StatusCode,
            responseJson: raw,
            orderId: orderId,
            shipmentId: baglyShipmentId,
            cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenStore.Invalidate();
            throw new ShiprocketAuthException($"Shiprocket assign AWB returned 401. Body: {Truncate(raw)}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Shiprocket assign AWB failed HTTP {(int)response.StatusCode}. Body: {Truncate(raw, 480)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var root = doc.RootElement;

        if (TryReadInt(root, "status_code") is int apiStatus && apiStatus >= 400)
        {
            throw new InvalidOperationException(
                $"Shiprocket assign AWB rejected status_code={apiStatus}: {TryReadMessage(root) ?? Truncate(raw)}");
        }

        if (TryReadInt(root, "awb_assign_status") is 0)
        {
            throw new InvalidOperationException(
                $"Shiprocket AWB assign failed: {TryReadMessage(root) ?? Truncate(raw, 300)}");
        }

        var data = FindAwbData(root);
        var awb = data is JsonElement d
            ? TryReadString(d, "awb_code") ?? TryReadString(d, "awb")
            : TryReadString(root, "awb_code");

        if (string.IsNullOrWhiteSpace(awb))
        {
            throw new InvalidOperationException(
                $"Shiprocket assign AWB succeeded but awb_code was missing. Body: {Truncate(raw, 480)}");
        }

        int? responseCourierId = null;
        string? courierName = null;
        decimal? freight = null;
        if (data is JsonElement dataEl)
        {
            responseCourierId = TryReadInt(dataEl, "courier_company_id") ?? TryReadInt(dataEl, "courier_id");
            courierName = TryReadString(dataEl, "courier_name") ?? TryReadString(dataEl, "courier_company_name");
            freight = TryReadDecimal(dataEl, "freight_charge")
                      ?? TryReadDecimal(dataEl, "rate")
                      ?? TryReadDecimal(dataEl, "charge")
                      ?? TryReadDecimal(dataEl, "applied_weight_amount");
        }

        return new AssignAwbApiResult(awb.Trim(), responseCourierId, courierName, freight);
    }

    private async Task<string> GenerateLabelWithAuthRetryAsync(
        long shipmentId,
        Guid orderId,
        Guid baglyShipmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GenerateLabelOnceAsync(
                shipmentId, forceLogin: false, orderId, baglyShipmentId, cancellationToken);
        }
        catch (ShiprocketAuthException)
        {
            tokenStore.Invalidate();
            return await GenerateLabelOnceAsync(
                shipmentId, forceLogin: true, orderId, baglyShipmentId, cancellationToken);
        }
    }

    /// <summary>
    /// Shiprocket generate label:
    /// POST https://apiv2.shiprocket.in/v1/external/courier/generate/label
    /// Body: { "shipment_id": [123456] }  (array of Shiprocket shipment ids)
    /// Response often includes label_url / nested PDF URL — parsed via <see cref="FindLabelUrl"/>.
    /// </summary>
    private async Task<string> GenerateLabelOnceAsync(
        long shipmentId,
        bool forceLogin,
        Guid orderId,
        Guid baglyShipmentId,
        CancellationToken cancellationToken)
    {
        var token = forceLogin
            ? await LoginAsync(cancellationToken)
            : tokenStore.GetValidToken() ?? await LoginAsync(cancellationToken);

        // Shiprocket expects shipment_id as an array (unlike assign/awb which takes a scalar).
        var body = new Dictionary<string, object>
        {
            ["shipment_id"] = new[] { shipmentId },
        };
        var requestJson = JsonSerializer.Serialize(body, JsonOptions);
        const string path = "v1/external/courier/generate/label";

        var client = httpClientFactory.CreateClient("Shiprocket");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        await apiLogs.LogAsync(
            "GenerateLabel",
            "POST",
            path,
            requestJson: requestJson,
            responseStatus: (int)response.StatusCode,
            responseJson: raw,
            orderId: orderId,
            shipmentId: baglyShipmentId,
            cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenStore.Invalidate();
            throw new ShiprocketAuthException($"Shiprocket generate label returned 401. Body: {Truncate(raw)}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Shiprocket generate label failed HTTP {(int)response.StatusCode}. Body: {Truncate(raw, 480)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var root = doc.RootElement;

        if (TryReadInt(root, "status_code") is int apiStatus && apiStatus >= 400)
        {
            throw new InvalidOperationException(
                $"Shiprocket generate label rejected status_code={apiStatus}: {TryReadMessage(root) ?? Truncate(raw)}");
        }

        // label_created: 0 means Shiprocket did not create a label for the shipment(s).
        if (TryReadInt(root, "label_created") is 0)
        {
            throw new InvalidOperationException(
                $"Shiprocket label not created: {TryReadMessage(root) ?? Truncate(raw, 300)}");
        }

        var labelUrl = FindLabelUrl(root);
        if (string.IsNullOrWhiteSpace(labelUrl))
        {
            throw new InvalidOperationException(
                $"Shiprocket generate label succeeded but label_url was missing. Body: {Truncate(raw, 480)}");
        }

        return labelUrl.Trim();
    }

    /// <summary>
    /// Robustly finds a label PDF/download URL from common Shiprocket response shapes:
    /// label_url, label_download, pdf_url, or nested under data / response / label_data.
    /// </summary>
    private static string? FindLabelUrl(JsonElement root)
    {
        foreach (var candidate in EnumerateLabelUrlCandidates(root))
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                return candidate.Trim();
            }
        }

        return null;
    }

    private static IEnumerable<string?> EnumerateLabelUrlCandidates(JsonElement root)
    {
        yield return TryReadString(root, "label_url");
        yield return TryReadString(root, "label_download");
        yield return TryReadString(root, "pdf_url");
        yield return TryReadString(root, "url");

        if (root.TryGetProperty("label_data", out var labelData) && labelData.ValueKind == JsonValueKind.Object)
        {
            yield return TryReadString(labelData, "label_url");
            yield return TryReadString(labelData, "url");
        }

        if (root.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.Object)
            {
                yield return TryReadString(data, "label_url");
                yield return TryReadString(data, "label_download");
                yield return TryReadString(data, "pdf_url");
                yield return TryReadString(data, "url");
            }
            else if (data.ValueKind == JsonValueKind.String)
            {
                yield return data.GetString();
            }
            else if (data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        yield return item.GetString();
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        yield return TryReadString(item, "label_url");
                        yield return TryReadString(item, "url");
                    }
                }
            }
        }

        if (root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object)
        {
            yield return TryReadString(response, "label_url");
            yield return TryReadString(response, "url");
            if (response.TryGetProperty("data", out var nested))
            {
                if (nested.ValueKind == JsonValueKind.Object)
                {
                    yield return TryReadString(nested, "label_url");
                    yield return TryReadString(nested, "url");
                }
                else if (nested.ValueKind == JsonValueKind.String)
                {
                    yield return nested.GetString();
                }
            }
        }
    }

    private async Task<GeneratePickupApiResult> GeneratePickupWithAuthRetryAsync(
        long shipmentId,
        Guid orderId,
        Guid baglyShipmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GeneratePickupOnceAsync(
                shipmentId, forceLogin: false, orderId, baglyShipmentId, cancellationToken);
        }
        catch (ShiprocketAuthException)
        {
            tokenStore.Invalidate();
            return await GeneratePickupOnceAsync(
                shipmentId, forceLogin: true, orderId, baglyShipmentId, cancellationToken);
        }
    }

    /// <summary>
    /// Shiprocket generate pickup:
    /// POST https://apiv2.shiprocket.in/v1/external/courier/generate/pickup
    /// Body: { "shipment_id": [123456] }  (array of Shiprocket shipment ids; one at a time)
    /// </summary>
    private async Task<GeneratePickupApiResult> GeneratePickupOnceAsync(
        long shipmentId,
        bool forceLogin,
        Guid orderId,
        Guid baglyShipmentId,
        CancellationToken cancellationToken)
    {
        var token = forceLogin
            ? await LoginAsync(cancellationToken)
            : tokenStore.GetValidToken() ?? await LoginAsync(cancellationToken);

        var body = new Dictionary<string, object>
        {
            ["shipment_id"] = new[] { shipmentId },
        };
        var requestJson = JsonSerializer.Serialize(body, JsonOptions);
        const string path = "v1/external/courier/generate/pickup";

        var client = httpClientFactory.CreateClient("Shiprocket");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        await apiLogs.LogAsync(
            "GeneratePickup",
            "POST",
            path,
            requestJson: requestJson,
            responseStatus: (int)response.StatusCode,
            responseJson: raw,
            orderId: orderId,
            shipmentId: baglyShipmentId,
            cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenStore.Invalidate();
            throw new ShiprocketAuthException($"Shiprocket generate pickup returned 401. Body: {Truncate(raw)}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Shiprocket generate pickup failed HTTP {(int)response.StatusCode}. Body: {Truncate(raw, 480)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var root = doc.RootElement;

        if (TryReadInt(root, "status_code") is int apiStatus && apiStatus >= 400)
        {
            throw new InvalidOperationException(
                $"Shiprocket generate pickup rejected status_code={apiStatus}: {TryReadMessage(root) ?? Truncate(raw)}");
        }

        // pickup_status: 0 means Shiprocket did not schedule pickup.
        if (TryReadInt(root, "pickup_status") is 0)
        {
            throw new InvalidOperationException(
                $"Shiprocket pickup not created: {TryReadMessage(root) ?? Truncate(raw, 300)}");
        }

        var pickupToken = FindPickupToken(root);
        return new GeneratePickupApiResult(pickupToken, raw);
    }

    private async Task<string> GenerateManifestWithAuthRetryAsync(
        long shipmentId,
        Guid orderId,
        Guid baglyShipmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GenerateManifestOnceAsync(
                shipmentId, forceLogin: false, orderId, baglyShipmentId, cancellationToken);
        }
        catch (ShiprocketAuthException)
        {
            tokenStore.Invalidate();
            return await GenerateManifestOnceAsync(
                shipmentId, forceLogin: true, orderId, baglyShipmentId, cancellationToken);
        }
    }

    /// <summary>
    /// Shiprocket generate manifest:
    /// POST https://apiv2.shiprocket.in/v1/external/manifests/generate
    /// Body: { "shipment_id": [123456] }  (array of Shiprocket shipment ids)
    /// Response often includes manifest_url / url — parsed via <see cref="FindManifestUrl"/>.
    /// </summary>
    private async Task<string> GenerateManifestOnceAsync(
        long shipmentId,
        bool forceLogin,
        Guid orderId,
        Guid baglyShipmentId,
        CancellationToken cancellationToken)
    {
        var token = forceLogin
            ? await LoginAsync(cancellationToken)
            : tokenStore.GetValidToken() ?? await LoginAsync(cancellationToken);

        var body = new Dictionary<string, object>
        {
            ["shipment_id"] = new[] { shipmentId },
        };
        var requestJson = JsonSerializer.Serialize(body, JsonOptions);
        const string path = "v1/external/manifests/generate";

        var client = httpClientFactory.CreateClient("Shiprocket");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        await apiLogs.LogAsync(
            "GenerateManifest",
            "POST",
            path,
            requestJson: requestJson,
            responseStatus: (int)response.StatusCode,
            responseJson: raw,
            orderId: orderId,
            shipmentId: baglyShipmentId,
            cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenStore.Invalidate();
            throw new ShiprocketAuthException($"Shiprocket generate manifest returned 401. Body: {Truncate(raw)}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Shiprocket generate manifest failed HTTP {(int)response.StatusCode}. Body: {Truncate(raw, 480)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var root = doc.RootElement;

        if (TryReadInt(root, "status_code") is int apiStatus && apiStatus >= 400)
        {
            throw new InvalidOperationException(
                $"Shiprocket generate manifest rejected status_code={apiStatus}: {TryReadMessage(root) ?? Truncate(raw)}");
        }

        // status: 0 (or message "Manifest not generated") means Shiprocket did not create a manifest.
        if (TryReadInt(root, "status") is 0)
        {
            throw new InvalidOperationException(
                $"Shiprocket manifest not generated: {TryReadMessage(root) ?? Truncate(raw, 300)}");
        }

        var message = TryReadMessage(root);
        if (!string.IsNullOrWhiteSpace(message) &&
            message.Contains("not generated", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Shiprocket manifest not generated: {Truncate(message, 300)}");
        }

        var manifestUrl = FindManifestUrl(root);
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            throw new InvalidOperationException(
                $"Shiprocket generate manifest succeeded but manifest_url was missing. Body: {Truncate(raw, 480)}");
        }

        return manifestUrl.Trim();
    }

    /// <summary>
    /// Robustly finds a manifest PDF/download URL from common Shiprocket response shapes:
    /// manifest_url, url, pdf_url, or nested under data / response.
    /// </summary>
    private static string? FindManifestUrl(JsonElement root)
    {
        foreach (var candidate in EnumerateManifestUrlCandidates(root))
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                return candidate.Trim();
            }
        }

        return null;
    }

    private static IEnumerable<string?> EnumerateManifestUrlCandidates(JsonElement root)
    {
        yield return TryReadString(root, "manifest_url");
        yield return TryReadString(root, "pdf_url");
        yield return TryReadString(root, "url");

        if (root.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.Object)
            {
                yield return TryReadString(data, "manifest_url");
                yield return TryReadString(data, "pdf_url");
                yield return TryReadString(data, "url");
            }
            else if (data.ValueKind == JsonValueKind.String)
            {
                yield return data.GetString();
            }
            else if (data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        yield return item.GetString();
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        yield return TryReadString(item, "manifest_url");
                        yield return TryReadString(item, "url");
                    }
                }
            }
        }

        if (root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object)
        {
            yield return TryReadString(response, "manifest_url");
            yield return TryReadString(response, "pdf_url");
            yield return TryReadString(response, "url");
            if (response.TryGetProperty("data", out var nested))
            {
                if (nested.ValueKind == JsonValueKind.Object)
                {
                    yield return TryReadString(nested, "manifest_url");
                    yield return TryReadString(nested, "url");
                }
                else if (nested.ValueKind == JsonValueKind.String)
                {
                    yield return nested.GetString();
                }
            }
        }
    }

    private static string? FindPickupToken(JsonElement root)
    {
        foreach (var candidate in EnumeratePickupTokenCandidates(root))
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return null;
    }

    private static IEnumerable<string?> EnumeratePickupTokenCandidates(JsonElement root)
    {
        yield return TryReadString(root, "pickup_token_number");
        yield return TryReadString(root, "pickup_token");
        yield return TryReadString(root, "token_number");

        if (root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object)
        {
            yield return TryReadString(response, "pickup_token_number");
            yield return TryReadString(response, "pickup_token");
            yield return TryReadString(response, "token_number");
            if (response.TryGetProperty("data", out var nested) && nested.ValueKind == JsonValueKind.Object)
            {
                yield return TryReadString(nested, "pickup_token_number");
                yield return TryReadString(nested, "pickup_token");
            }
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            yield return TryReadString(data, "pickup_token_number");
            yield return TryReadString(data, "pickup_token");
        }
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

    private static JsonElement? FindCourierArray(JsonElement root)
    {
        if (root.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("available_courier_companies", out var list) &&
                list.ValueKind == JsonValueKind.Array)
            {
                return list;
            }

            if (data.ValueKind == JsonValueKind.Array)
            {
                return data;
            }
        }

        if (root.TryGetProperty("available_courier_companies", out var top) &&
            top.ValueKind == JsonValueKind.Array)
        {
            return top;
        }

        return null;
    }

    private static JsonElement? FindAwbData(JsonElement root)
    {
        if (root.TryGetProperty("response", out var response) &&
            response.ValueKind == JsonValueKind.Object &&
            response.TryGetProperty("data", out var nested) &&
            nested.ValueKind == JsonValueKind.Object)
        {
            return nested;
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            return data;
        }

        return root.ValueKind == JsonValueKind.Object ? root : null;
    }

    private static string? TryReadMessage(JsonElement root)
    {
        foreach (var key in new[] { "message", "error", "msg" })
        {
            if (root.TryGetProperty(key, out var el))
            {
                if (el.ValueKind == JsonValueKind.String) return el.GetString();
                if (el.ValueKind == JsonValueKind.Object || el.ValueKind == JsonValueKind.Array)
                {
                    return Truncate(el.GetRawText(), 200);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Maps Shiprocket courier pickup timing fields into a display string.
    /// Prefers explicit pickup strings, then seconds_left_for_pickup, then cutoff_time / pickup_availability.
    /// </summary>
    private static string? FormatExpectedPickup(JsonElement item)
    {
        var direct = TryReadString(item, "expected_pickup")
                     ?? TryReadString(item, "pickup_date")
                     ?? TryReadString(item, "expected_pickup_date");
        if (!string.IsNullOrWhiteSpace(direct))
            return direct.Trim();

        var seconds = TryReadInt(item, "seconds_left_for_pickup");
        if (seconds is > 0)
        {
            var ts = TimeSpan.FromSeconds(seconds.Value);
            if (ts.TotalMinutes < 60)
                return $"in {(int)Math.Max(1, ts.TotalMinutes)} min";
            if (ts.TotalHours < 24)
            {
                var h = (int)ts.TotalHours;
                var m = ts.Minutes;
                return m > 0 ? $"in {h}h {m}m" : $"in {h}h";
            }

            var days = (int)Math.Ceiling(ts.TotalDays);
            return days == 1 ? "in 1 day" : $"in {days} days";
        }

        var cutoff = TryReadString(item, "cutoff_time");
        if (!string.IsNullOrWhiteSpace(cutoff))
            return $"by {cutoff.Trim()}";

        // pickup_availability is often "0"/"1"; only use when it looks like a label/time.
        var availability = TryReadString(item, "pickup_availability");
        if (!string.IsNullOrWhiteSpace(availability) &&
            availability is not ("0" or "1"))
        {
            return availability.Trim();
        }

        return null;
    }

    private static string? TryReadString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            _ => null,
        };
    }

    private static int? TryReadInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) return n;
        if (el.ValueKind == JsonValueKind.String &&
            int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>
    /// Reads a JSON number/string as decimal. Never uses int conversion (would drop .36 from 138.36).
    /// </summary>
    private static decimal? TryReadDecimal(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return null;

        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                if (el.TryGetDecimal(out var d)) return d;
                // Raw text preserves fractional digits if TryGetDecimal fails.
                if (decimal.TryParse(
                        el.GetRawText(),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var fromRaw))
                {
                    return fromRaw;
                }

                if (el.TryGetDouble(out var dbl) && !double.IsNaN(dbl) && !double.IsInfinity(dbl))
                {
                    return Convert.ToDecimal(dbl);
                }

                return null;

            case JsonValueKind.String:
                var s = el.GetString();
                if (string.IsNullOrWhiteSpace(s)) return null;
                if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }

                return null;

            default:
                return null;
        }
    }

    private static string Truncate(string? value, int max = 240)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..max] + "…";
    }

    private sealed record AssignAwbApiResult(
        string AwbCode,
        int? CourierId,
        string? CourierName,
        decimal? FreightCharge);

    private sealed record GeneratePickupApiResult(
        string? PickupTokenNumber,
        string? RawJson);

    private sealed class QueryStringBuilder
    {
        private readonly List<string> _parts = [];

        public QueryStringBuilder Add(string key, string value)
        {
            _parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
            return this;
        }

        public override string ToString() => string.Join("&", _parts);
    }
}
