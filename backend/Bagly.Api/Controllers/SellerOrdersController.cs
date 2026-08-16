using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Bagly.Api.Data;
using Bagly.Api.Models;
using Bagly.Api.Options;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Controllers;

/// <summary>
/// Approved sellers view orders that include their products, mark pickup shipments
/// Ready to Ship (gates admin courier selection), and cancel before AWB.
/// Visibility (any of):
/// 1) Product.SellerId == seller
/// 2) OrderItems → Products.ShiprocketPickupLocation matches a seller SellerPickupLocations nickname
///    (no OrderShiprocketShipments required — works before / without Shiprocket create)
/// 3) OrderShiprocketShipments.PickupLocation matches a seller nickname
/// Pickup nickname matching is OrdinalIgnoreCase (seller product validation already allows CI).
/// </summary>
[ApiController]
[Authorize(Roles = "Seller")]
[Route("api/seller/orders")]
public class SellerOrdersController(
    BaglyDbContext db,
    IOptions<ShiprocketOptions> shiprocketOptions,
    IAuditLogService auditLog) : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int ReportPageSize = 50;
    private const int MaxReportRows = 5000;

    [HttpGet]
    public async Task<ActionResult<SellerOrdersListResult>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePaging(page, pageSize);
        return await ListCoreAsync(page, pageSize, from, to, status, cancellationToken);
    }

    /// <summary>
    /// Date-filtered order report for the seller hub. Requires <paramref name="from"/> and
    /// <paramref name="to"/>; page size defaults to and caps at <see cref="ReportPageSize"/>.
    /// </summary>
    [HttpGet("report")]
    public async Task<ActionResult<SellerOrdersListResult>> Report(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = ReportPageSize,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var dateError = RequireReportDateRange(from, to);
        if (dateError is not null) return dateError;

        (page, pageSize) = NormalizeReportPaging(page, pageSize);
        return await ListCoreAsync(page, pageSize, from, to, status, cancellationToken);
    }

    /// <summary>
    /// All orders matching the report filters (capped) for Excel/PDF export — not page-limited.
    /// </summary>
    [HttpGet("report/export")]
    public async Task<ActionResult<SellerOrdersListResult>> ReportExport(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var dateError = RequireReportDateRange(from, to);
        if (dateError is not null) return dateError;

        return await ListCoreAsync(1, MaxReportRows, from, to, status, cancellationToken);
    }

    private async Task<ActionResult<SellerOrdersListResult>> ListCoreAsync(
        int page,
        int pageSize,
        DateOnly? from,
        DateOnly? to,
        string? status,
        CancellationToken cancellationToken)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null) return Unauthorized(new { message = "Seller session is invalid." });

        var approvalError = RequireApproved(seller);
        if (approvalError is not null) return approvalError;

        var scope = await LoadSellerOrderScopeAsync(seller.Id, cancellationToken);
        if (scope.OwnedProductIds.Count == 0 && scope.RegisteredPickups.Count == 0)
        {
            return Ok(new SellerOrdersListResult(
                [], page, pageSize, 0, 0,
                scope.OwnedProductIds.Count,
                scope.RegisteredPickups.Count,
                scope.VisibleProductIds.Count));
        }

        // Plain lists for EF translation (avoid HashSet+comparer in expression trees).
        var visibleProductIds = scope.VisibleProductIds.ToList();
        var pickupLower = scope.RegisteredPickups
            .Select(p => p.ToLowerInvariant())
            .Distinct()
            .ToList();
        var ownedSet = scope.OwnedProductIds.ToHashSet(StringComparer.Ordinal);
        var visibleProductSet = visibleProductIds.ToHashSet(StringComparer.Ordinal);

        // Path 1+2: line items whose products the seller owns or fulfills by pickup nickname.
        // Path 3: shipment rows whose pickup matches (covers stubs created when Shiprocket skips).
        var sellerId = seller.Id;
        var hasPickups = pickupLower.Count > 0;
        var baseQuery = db.Orders.AsNoTracking()
            .Where(o =>
                o.Items.Any(i =>
                    db.Products.Any(p =>
                        p.Id == i.ProductId && (
                            p.SellerId == sellerId ||
                            (hasPickups
                             && p.ShiprocketPickupLocation != null
                             && p.ShiprocketPickupLocation != ""
                             && pickupLower.Contains(p.ShiprocketPickupLocation.ToLower()))))) ||
                (hasPickups && o.ShiprocketShipments.Any(s =>
                    pickupLower.Contains(s.PickupLocation.ToLower()))));

        if (from is DateOnly fromDate)
            baseQuery = baseQuery.Where(o => o.CreatedAt >= IstTime.ToUtc(fromDate));

        if (to is DateOnly toDate)
        {
            var toExclusiveUtc = IstTime.ToUtc(toDate.AddDays(1));
            baseQuery = baseQuery.Where(o => o.CreatedAt < toExclusiveUtc);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusTerm = status.Trim();
            baseQuery = baseQuery.Where(o => o.Status == statusTerm);
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var orders = await baseQuery
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(o => o.Items)
            .Include(o => o.ShiprocketShipments)
            .ToListAsync(cancellationToken);

        var productIdsOnPage = orders
            .SelectMany(o => o.Items.Select(i => i.ProductId))
            .Where(id => visibleProductSet.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Also include any line ProductIds on the page so we can attribute items matched only via join.
        var allLineProductIds = orders
            .SelectMany(o => o.Items.Select(i => i.ProductId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var productIdsForPickupLookup = productIdsOnPage
            .Concat(allLineProductIds)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var productPickups = await db.Products.AsNoTracking()
            .Where(p => productIdsForPickupLookup.Contains(p.Id))
            .Select(p => new { p.Id, p.SellerId, p.ShiprocketPickupLocation })
            .ToListAsync(cancellationToken);

        var productPickupMap = productPickups.ToDictionary(
            p => p.Id,
            p => p.ShiprocketPickupLocation,
            StringComparer.Ordinal);

        // Rebuild visible set from live product rows (covers CI pickup match on this page).
        var pickupIgnore = scope.RegisteredPickups.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in productPickups)
        {
            if (p.SellerId == seller.Id)
            {
                visibleProductSet.Add(p.Id);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(p.ShiprocketPickupLocation)
                && pickupIgnore.Contains(p.ShiprocketPickupLocation.Trim()))
            {
                visibleProductSet.Add(p.Id);
            }
        }

        var defaultPickup = shiprocketOptions.Value.PickupLocation?.Trim() ?? "";

        var items = orders.Select(o =>
        {
            var sellerItems = o.Items
                .Where(i => visibleProductSet.Contains(i.ProductId))
                .Select(i =>
                {
                    var lineTotal = i.UnitPrice * i.Quantity;
                    return new SellerOrderItemDto(
                        i.ProductId,
                        i.ProductName,
                        i.Color,
                        i.UnitPrice,
                        i.Quantity,
                        lineTotal);
                })
                .ToList();

            var subtotal = sellerItems.Sum(i => i.LineTotal);
            var pickupNicknames = ResolveSellerPickupNicknames(
                sellerItems.Select(i => i.ProductId).Where(id => ownedSet.Contains(id)),
                productPickupMap,
                scope.RegisteredPickups,
                defaultPickup);

            var shipments = o.ShiprocketShipments
                .Where(s => pickupNicknames.Contains(s.PickupLocation))
                .OrderBy(s => s.PickupLocation, StringComparer.OrdinalIgnoreCase)
                .Select(MapShipment)
                .ToList();

            return new SellerOrderDto(
                o.Id,
                o.OrderNumber,
                o.Status,
                o.PaymentStatus,
                o.PaymentProvider,
                o.Currency ?? "INR",
                subtotal,
                o.Subtotal,
                o.Shipping,
                o.Total,
                o.CreatedAt,
                MaskCustomerName(o.FirstName, o.LastName),
                o.City,
                o.State,
                o.Zip,
                sellerItems,
                shipments);
        }).ToList();

        return Ok(new SellerOrdersListResult(
            items, page, pageSize, totalCount, totalPages,
            scope.OwnedProductIds.Count,
            scope.RegisteredPickups.Count,
            scope.VisibleProductIds.Count));
    }

    /// <summary>
    /// Seller marks their pickup shipment ready so Admin Shipping can run courier serviceability.
    /// </summary>
    [HttpPost("{orderId:guid}/shipments/{shipmentId:guid}/ready-to-ship")]
    public async Task<ActionResult<SellerShipmentDto>> MarkReadyToShip(
        Guid orderId,
        Guid shipmentId,
        CancellationToken cancellationToken)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null) return Unauthorized(new { message = "Seller session is invalid." });

        var approvalError = RequireApproved(seller);
        if (approvalError is not null) return approvalError;

        var order = await db.Orders
            .Include(o => o.Items)
            .Include(o => o.ShiprocketShipments)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
            return NotFound(new { message = "Order not found." });

        if (string.Equals(order.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Order is already cancelled." });

        var ownership = await EnsureSellerOwnsOrderAsync(order, seller.Id, cancellationToken);
        if (ownership is not null) return ownership;

        var shipment = order.ShiprocketShipments.FirstOrDefault(s => s.Id == shipmentId);
        if (shipment is null)
            return NotFound(new { message = "Shipment not found on this order." });

        var pickupOk = await SellerOwnsShipmentPickupAsync(order, seller.Id, shipment.PickupLocation, cancellationToken);
        if (!pickupOk)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This shipment is not for your pickup." });

        if (string.Equals(shipment.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(shipment.ShippingStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Cannot mark a cancelled shipment ready." });
        }

        if (!string.IsNullOrWhiteSpace(shipment.AwbCode))
            return BadRequest(new { message = "AWB already assigned; cannot change ready status." });

        if (string.IsNullOrWhiteSpace(shipment.ShiprocketShipmentId))
        {
            return BadRequest(new
            {
                message = "Shiprocket shipment is not created yet. Wait for fulfillment setup, then try again.",
            });
        }

        shipment.SellerReadyToShipAt ??= DateTime.UtcNow;
        shipment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "Shipping",
            action: "SellerReadyToShip",
            message: $"Seller '{seller.Email}' marked order {order.OrderNumber} / {shipment.PickupLocation} ready to ship.",
            actorEmail: seller.Email,
            entityType: "OrderShiprocketShipment",
            entityId: shipment.Id.ToString(),
            details: new { order.OrderNumber, shipment.PickupLocation, shipment.SellerReadyToShipAt },
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            requestPath: HttpContext.Request.Path.Value,
            cancellationToken: cancellationToken);

        return Ok(MapShipment(shipment));
    }

    /// <summary>
    /// Cancel seller portion (or whole order when all lines are theirs) before AWB assignment.
    /// </summary>
    [HttpPost("{orderId:guid}/cancel")]
    public async Task<ActionResult<SellerOrderDto>> Cancel(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null) return Unauthorized(new { message = "Seller session is invalid." });

        var approvalError = RequireApproved(seller);
        if (approvalError is not null) return approvalError;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var order = await db.Orders
            .Include(o => o.Items)
            .Include(o => o.ShiprocketShipments)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
            return NotFound(new { message = "Order not found." });

        if (string.Equals(order.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Order is already cancelled." });

        var ownership = await EnsureSellerOwnsOrderAsync(order, seller.Id, cancellationToken);
        if (ownership is not null) return ownership;

        var scope = await LoadSellerOrderScopeAsync(seller.Id, cancellationToken);
        var ownedSet = scope.OwnedProductIds.ToHashSet(StringComparer.Ordinal);
        var visibleProductSet = scope.VisibleProductIds.ToHashSet(StringComparer.Ordinal);

        var sellerItems = order.Items.Where(i => visibleProductSet.Contains(i.ProductId)).ToList();
        if (sellerItems.Count == 0)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This order has no items of yours." });

        var defaultPickup = shiprocketOptions.Value.PickupLocation?.Trim() ?? "";
        var productPickups = await db.Products.AsNoTracking()
            .Where(p => sellerItems.Select(i => i.ProductId).Contains(p.Id))
            .Select(p => new { p.Id, p.ShiprocketPickupLocation })
            .ToDictionaryAsync(p => p.Id, p => p.ShiprocketPickupLocation, StringComparer.Ordinal, cancellationToken);
        var pickupNicknames = ResolveSellerPickupNicknames(
            sellerItems.Select(i => i.ProductId).Where(id => ownedSet.Contains(id)),
            productPickups,
            scope.RegisteredPickups,
            defaultPickup);

        var sellerShipments = order.ShiprocketShipments
            .Where(s => pickupNicknames.Contains(s.PickupLocation))
            .ToList();

        if (sellerShipments.Any(s => !string.IsNullOrWhiteSpace(s.AwbCode)))
        {
            return BadRequest(new { message = "Cannot cancel after AWB is assigned. Contact support." });
        }

        if (sellerShipments.Any(s => s.ReadyToShipAt != null))
        {
            return BadRequest(new
            {
                message = "Cannot cancel after admin has started Ready to Ship / courier selection.",
            });
        }

        var now = DateTime.UtcNow;
        foreach (var shipment in sellerShipments)
        {
            shipment.Status = "Cancelled";
            shipment.ShippingStatus = "Cancelled";
            shipment.UpdatedAt = now;
            shipment.LastError = "Cancelled by seller";
        }

        var allItemsAreSellers = order.Items.All(i => visibleProductSet.Contains(i.ProductId));
        if (allItemsAreSellers)
        {
            order.Status = "Cancelled";
            order.ShiprocketStatus = "Cancelled";
            order.ShiprocketLastError = "Cancelled by seller";
        }

        foreach (var item in sellerItems)
        {
            await db.Products
                .Where(p => p.Id == item.ProductId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(p => p.StockQuantity, p => p.StockQuantity + item.Quantity),
                    cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "Order",
            action: "SellerCancel",
            message: allItemsAreSellers
                ? $"Seller '{seller.Email}' cancelled order {order.OrderNumber}."
                : $"Seller '{seller.Email}' cancelled their portion of order {order.OrderNumber}.",
            actorEmail: seller.Email,
            entityType: "Order",
            entityId: order.Id.ToString(),
            details: new
            {
                order.OrderNumber,
                FullOrderCancelled = allItemsAreSellers,
                ItemCount = sellerItems.Count,
                ShipmentIds = sellerShipments.Select(s => s.Id).ToList(),
            },
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            requestPath: HttpContext.Request.Path.Value,
            cancellationToken: cancellationToken);

        var dto = new SellerOrderDto(
            order.Id,
            order.OrderNumber,
            order.Status,
            order.PaymentStatus,
            order.PaymentProvider,
            order.Currency ?? "INR",
            sellerItems.Sum(i => i.UnitPrice * i.Quantity),
            order.Subtotal,
            order.Shipping,
            order.Total,
            order.CreatedAt,
            MaskCustomerName(order.FirstName, order.LastName),
            order.City,
            order.State,
            order.Zip,
            sellerItems.Select(i => new SellerOrderItemDto(
                i.ProductId,
                i.ProductName,
                i.Color,
                i.UnitPrice,
                i.Quantity,
                i.UnitPrice * i.Quantity)).ToList(),
            sellerShipments.Select(MapShipment).ToList());

        return Ok(dto);
    }

    private async Task<SellerOrderScope> LoadSellerOrderScopeAsync(
        Guid sellerId,
        CancellationToken cancellationToken)
    {
        var ownedProductIds = await db.Products.AsNoTracking()
            .Where(p => p.SellerId == sellerId)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        // Successful pickups only — same source as seller product pickup validation.
        var registeredPickups = await db.SellerPickupLocations.AsNoTracking()
            .Where(p => p.SellerUserId == sellerId && p.ShiprocketSuccess)
            .Select(p => p.PickupLocation)
            .ToListAsync(cancellationToken);

        var pickupList = registeredPickups
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        List<string> pickupMatchedProductIds = [];
        if (pickupList.Count > 0)
        {
            // OrdinalIgnoreCase: seller product validation already accepts CI nicknames;
            // seed wareHouse1 must still match seller pickups typed with different casing.
            var pickupLower = pickupList.Select(p => p.ToLowerInvariant()).ToList();
            pickupMatchedProductIds = await db.Products.AsNoTracking()
                .Where(p => p.ShiprocketPickupLocation != null
                            && p.ShiprocketPickupLocation != ""
                            && pickupLower.Contains(p.ShiprocketPickupLocation.ToLower()))
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
        }

        var visible = ownedProductIds
            .Concat(pickupMatchedProductIds)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new SellerOrderScope(ownedProductIds, pickupList, visible);
    }

    private async Task<ActionResult?> EnsureSellerOwnsOrderAsync(
        Order order,
        Guid sellerId,
        CancellationToken cancellationToken)
    {
        var scope = await LoadSellerOrderScopeAsync(sellerId, cancellationToken);
        var productIds = order.Items.Select(i => i.ProductId).Distinct(StringComparer.Ordinal).ToList();
        var hasVisibleItem = productIds.Count > 0
            && productIds.Any(id => scope.VisibleProductIds.Contains(id, StringComparer.Ordinal));

        if (hasVisibleItem)
            return null;

        var pickupSet = scope.RegisteredPickups.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasPickupShipment = pickupSet.Count > 0
            && order.ShiprocketShipments.Any(s => pickupSet.Contains(s.PickupLocation));

        if (hasPickupShipment)
            return null;

        return StatusCode(StatusCodes.Status403Forbidden, new { message = "This order is not associated with your products." });
    }

    private async Task<bool> SellerOwnsShipmentPickupAsync(
        Order order,
        Guid sellerId,
        string pickupLocation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pickupLocation))
            return false;

        var scope = await LoadSellerOrderScopeAsync(sellerId, cancellationToken);
        var defaultPickup = shiprocketOptions.Value.PickupLocation?.Trim() ?? "";
        var ownedSet = scope.OwnedProductIds.ToHashSet(StringComparer.Ordinal);

        var ownedItemProductIds = order.Items
            .Select(i => i.ProductId)
            .Where(id => ownedSet.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Dictionary<string, string?> productPickups = new(StringComparer.Ordinal);
        if (ownedItemProductIds.Count > 0)
        {
            productPickups = await db.Products.AsNoTracking()
                .Where(p => ownedItemProductIds.Contains(p.Id))
                .Select(p => new { p.Id, p.ShiprocketPickupLocation })
                .ToDictionaryAsync(p => p.Id, p => p.ShiprocketPickupLocation, StringComparer.Ordinal, cancellationToken);
        }

        var nicknames = ResolveSellerPickupNicknames(
            ownedItemProductIds,
            productPickups,
            scope.RegisteredPickups,
            defaultPickup);

        return nicknames.Contains(pickupLocation);
    }

    /// <summary>
    /// Seller-controlled pickups: registered SellerPickupLocations nicknames,
    /// plus resolved pickups for seller-owned products (falls back to platform default).
    /// Matching is OrdinalIgnoreCase so casing drift between product and pickup rows still works.
    /// </summary>
    private static HashSet<string> ResolveSellerPickupNicknames(
        IEnumerable<string> sellerOwnedProductIds,
        IReadOnlyDictionary<string, string?> productPickups,
        IEnumerable<string> registeredPickups,
        string defaultPickup)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var nick in registeredPickups)
        {
            if (!string.IsNullOrWhiteSpace(nick))
                set.Add(nick.Trim());
        }

        foreach (var productId in sellerOwnedProductIds)
        {
            productPickups.TryGetValue(productId, out var pickup);
            var resolved = string.IsNullOrWhiteSpace(pickup) ? defaultPickup : pickup.Trim();
            if (!string.IsNullOrWhiteSpace(resolved))
                set.Add(resolved);
        }

        return set;
    }

    private static SellerShipmentDto MapShipment(OrderShiprocketShipment s) =>
        new(
            s.Id,
            s.PickupLocation,
            s.ShiprocketOrderId,
            s.ShiprocketShipmentId,
            s.Status,
            s.ShippingStatus,
            s.LastError,
            s.AwbCode,
            s.CourierName,
            s.SellerReadyToShipAt,
            s.SellerReadyToShipAt != null,
            s.ReadyToShipAt,
            s.AwbAssignedAt,
            s.CreatedAt,
            s.LabelUrl,
            s.LabelGeneratedAt,
            !string.IsNullOrWhiteSpace(s.LabelUrl),
            s.PickupRequestedAt,
            s.TrackingStatus,
            s.TrackingStatusUpdatedAt,
            s.ManifestUrl,
            s.ManifestGeneratedAt,
            !string.IsNullOrWhiteSpace(s.ManifestUrl));

    private static string MaskCustomerName(string first, string last)
    {
        var f = (first ?? "").Trim();
        var l = (last ?? "").Trim();
        if (f.Length == 0 && l.Length == 0) return "Customer";
        var firstPart = f.Length == 0 ? "" : f[..1] + ".";
        var lastPart = l.Length <= 1 ? l : l[..1] + new string('*', Math.Min(3, l.Length - 1));
        return $"{firstPart} {lastPart}".Trim();
    }

    private static ActionResult? RequireApproved(SellerUser seller)
    {
        if (string.Equals(seller.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            return null;

        return new ObjectResult(new
        {
            message = "Your seller account must be approved before you can manage orders.",
        })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }

    private async Task<SellerUser?> LoadCurrentSellerAsync(CancellationToken cancellationToken)
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(idClaim, out var sellerId))
            return null;

        return await db.SellerUsers
            .FirstOrDefaultAsync(u => u.Id == sellerId && u.IsActive, cancellationToken);
    }

    private ActionResult<SellerOrdersListResult>? RequireReportDateRange(DateOnly? from, DateOnly? to)
    {
        if (from is null || to is null)
            return BadRequest(new { message = "From and To dates are required." });
        if (from > to)
            return BadRequest(new { message = "From date must be on or before To date." });
        return null;
    }

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (page, pageSize);
    }

    private static (int Page, int PageSize) NormalizeReportPaging(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = ReportPageSize;
        if (pageSize > ReportPageSize) pageSize = ReportPageSize;
        return (page, pageSize);
    }

    private sealed record SellerOrderScope(
        IReadOnlyList<string> OwnedProductIds,
        IReadOnlyList<string> RegisteredPickups,
        IReadOnlyList<string> VisibleProductIds);
}

public record SellerOrderItemDto(
    string ProductId,
    string ProductName,
    string Color,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public record SellerShipmentDto(
    Guid Id,
    string PickupLocation,
    string? ShiprocketOrderId,
    string? ShiprocketShipmentId,
    string? Status,
    string? ShippingStatus,
    string? LastError,
    string? AwbCode,
    string? CourierName,
    DateTime? SellerReadyToShipAt,
    bool SellerReady,
    DateTime? ReadyToShipAt,
    DateTime? AwbAssignedAt,
    DateTime CreatedAt,
    string? LabelUrl = null,
    DateTime? LabelGeneratedAt = null,
    bool CanDownloadLabel = false,
    DateTime? PickupRequestedAt = null,
    string? TrackingStatus = null,
    DateTime? TrackingStatusUpdatedAt = null,
    string? ManifestUrl = null,
    DateTime? ManifestGeneratedAt = null,
    bool CanDownloadManifest = false);

public record SellerOrderDto(
    Guid Id,
    string OrderNumber,
    string Status,
    string PaymentStatus,
    string? PaymentProvider,
    string Currency,
    decimal Subtotal,
    decimal OrderSubtotal,
    decimal Shipping,
    decimal OrderTotal,
    DateTime CreatedAt,
    string CustomerName,
    string? City,
    string? State,
    string? Zip,
    IReadOnlyList<SellerOrderItemDto> Items,
    IReadOnlyList<SellerShipmentDto> Shipments);

public record SellerOrdersListResult(
    IReadOnlyList<SellerOrderDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    int OwnedProductCount = 0,
    int RegisteredPickupCount = 0,
    int VisibleProductCount = 0);
