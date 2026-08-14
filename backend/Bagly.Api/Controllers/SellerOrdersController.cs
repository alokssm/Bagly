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
/// </summary>
[ApiController]
[Authorize(Roles = "Seller")]
[Route("api/seller/orders")]
public class SellerOrdersController(
    BaglyDbContext db,
    IOptions<ShiprocketOptions> shiprocketOptions,
    IAuditLogService auditLog) : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    [HttpGet]
    public async Task<ActionResult<SellerOrdersListResult>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null) return Unauthorized(new { message = "Seller session is invalid." });

        var approvalError = RequireApproved(seller);
        if (approvalError is not null) return approvalError;

        (page, pageSize) = NormalizePaging(page, pageSize);

        var sellerProductIds = await db.Products.AsNoTracking()
            .Where(p => p.SellerId == seller.Id)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (sellerProductIds.Count == 0)
        {
            return Ok(new SellerOrdersListResult([], page, pageSize, 0, 0));
        }

        var defaultPickup = shiprocketOptions.Value.PickupLocation?.Trim() ?? "";

        var baseQuery = db.Orders.AsNoTracking()
            .Where(o => o.Items.Any(i => sellerProductIds.Contains(i.ProductId)));

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var orders = await baseQuery
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(o => o.Items)
            .Include(o => o.ShiprocketShipments)
            .ToListAsync(cancellationToken);

        var sellerIdSet = sellerProductIds.ToHashSet(StringComparer.Ordinal);
        var productIdsOnPage = orders
            .SelectMany(o => o.Items.Select(i => i.ProductId))
            .Where(id => sellerIdSet.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var productPickups = await db.Products.AsNoTracking()
            .Where(p => productIdsOnPage.Contains(p.Id) && p.SellerId == seller.Id)
            .Select(p => new { p.Id, p.ShiprocketPickupLocation })
            .ToDictionaryAsync(p => p.Id, p => p.ShiprocketPickupLocation, StringComparer.Ordinal, cancellationToken);

        var registeredPickups = await db.SellerPickupLocations.AsNoTracking()
            .Where(p => p.SellerUserId == seller.Id)
            .Select(p => p.PickupLocation)
            .ToListAsync(cancellationToken);

        var items = orders.Select(o =>
        {
            var sellerItems = o.Items
                .Where(i => sellerIdSet.Contains(i.ProductId))
                .Select(i => new SellerOrderItemDto(
                    i.ProductId,
                    i.ProductName,
                    i.Color,
                    i.UnitPrice,
                    i.Quantity))
                .ToList();

            var sellerSubtotal = sellerItems.Sum(i => i.UnitPrice * i.Quantity);
            var pickupNicknames = ResolveSellerPickupNicknames(
                sellerItems.Select(i => i.ProductId),
                productPickups,
                registeredPickups,
                defaultPickup);

            var shipments = o.ShiprocketShipments
                .Where(s => pickupNicknames.Contains(s.PickupLocation))
                .OrderBy(s => s.PickupLocation)
                .Select(MapShipment)
                .ToList();

            return new SellerOrderDto(
                o.Id,
                o.OrderNumber,
                o.Status,
                o.PaymentStatus,
                o.PaymentProvider,
                o.Currency ?? "INR",
                sellerSubtotal,
                o.CreatedAt,
                MaskCustomerName(o.FirstName, o.LastName),
                o.City,
                o.State,
                o.Zip,
                sellerItems,
                shipments);
        }).ToList();

        return Ok(new SellerOrdersListResult(items, page, pageSize, totalCount, totalPages));
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

        var sellerProductIds = await db.Products.AsNoTracking()
            .Where(p => p.SellerId == seller.Id)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        var sellerIdSet = sellerProductIds.ToHashSet(StringComparer.Ordinal);

        var sellerItems = order.Items.Where(i => sellerIdSet.Contains(i.ProductId)).ToList();
        if (sellerItems.Count == 0)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This order has no items of yours." });

        var defaultPickup = shiprocketOptions.Value.PickupLocation?.Trim() ?? "";
        var productPickups = await db.Products.AsNoTracking()
            .Where(p => sellerItems.Select(i => i.ProductId).Contains(p.Id))
            .Select(p => new { p.Id, p.ShiprocketPickupLocation })
            .ToDictionaryAsync(p => p.Id, p => p.ShiprocketPickupLocation, StringComparer.Ordinal, cancellationToken);
        var registeredPickups = await db.SellerPickupLocations.AsNoTracking()
            .Where(p => p.SellerUserId == seller.Id)
            .Select(p => p.PickupLocation)
            .ToListAsync(cancellationToken);
        var pickupNicknames = ResolveSellerPickupNicknames(
            sellerItems.Select(i => i.ProductId),
            productPickups,
            registeredPickups,
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

        var allItemsAreSellers = order.Items.All(i => sellerIdSet.Contains(i.ProductId));
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
            order.CreatedAt,
            MaskCustomerName(order.FirstName, order.LastName),
            order.City,
            order.State,
            order.Zip,
            sellerItems.Select(i => new SellerOrderItemDto(
                i.ProductId, i.ProductName, i.Color, i.UnitPrice, i.Quantity)).ToList(),
            sellerShipments.Select(MapShipment).ToList());

        return Ok(dto);
    }

    private async Task<ActionResult?> EnsureSellerOwnsOrderAsync(
        Order order,
        Guid sellerId,
        CancellationToken cancellationToken)
    {
        var productIds = order.Items.Select(i => i.ProductId).Distinct(StringComparer.Ordinal).ToList();
        var hasItem = productIds.Count > 0 && await db.Products.AsNoTracking()
            .AnyAsync(p => p.SellerId == sellerId && productIds.Contains(p.Id), cancellationToken);

        if (!hasItem)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This order is not associated with your products." });
        }

        return null;
    }

    private async Task<bool> SellerOwnsShipmentPickupAsync(
        Order order,
        Guid sellerId,
        string pickupLocation,
        CancellationToken cancellationToken)
    {
        var defaultPickup = shiprocketOptions.Value.PickupLocation?.Trim() ?? "";
        var sellerProductIds = await db.Products.AsNoTracking()
            .Where(p => p.SellerId == sellerId)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        var sellerIdSet = sellerProductIds.ToHashSet(StringComparer.Ordinal);
        var sellerItems = order.Items.Where(i => sellerIdSet.Contains(i.ProductId)).ToList();
        if (sellerItems.Count == 0) return false;

        var productPickups = await db.Products.AsNoTracking()
            .Where(p => sellerItems.Select(i => i.ProductId).Contains(p.Id))
            .Select(p => new { p.Id, p.ShiprocketPickupLocation })
            .ToDictionaryAsync(p => p.Id, p => p.ShiprocketPickupLocation, StringComparer.Ordinal, cancellationToken);
        var registeredPickups = await db.SellerPickupLocations.AsNoTracking()
            .Where(p => p.SellerUserId == sellerId)
            .Select(p => p.PickupLocation)
            .ToListAsync(cancellationToken);

        var nicknames = ResolveSellerPickupNicknames(
            sellerItems.Select(i => i.ProductId),
            productPickups,
            registeredPickups,
            defaultPickup);

        return nicknames.Contains(pickupLocation);
    }

    private static HashSet<string> ResolveSellerPickupNicknames(
        IEnumerable<string> sellerProductIds,
        IReadOnlyDictionary<string, string?> productPickups,
        IEnumerable<string> registeredPickups,
        string defaultPickup)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nick in registeredPickups)
        {
            if (!string.IsNullOrWhiteSpace(nick))
                set.Add(nick.Trim());
        }

        foreach (var productId in sellerProductIds)
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
            s.CreatedAt);

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

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (page, pageSize);
    }
}

public record SellerOrderItemDto(
    string ProductId,
    string ProductName,
    string Color,
    decimal UnitPrice,
    int Quantity);

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
    DateTime CreatedAt);

public record SellerOrderDto(
    Guid Id,
    string OrderNumber,
    string Status,
    string PaymentStatus,
    string? PaymentProvider,
    string Currency,
    decimal SellerSubtotal,
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
    int TotalPages);
