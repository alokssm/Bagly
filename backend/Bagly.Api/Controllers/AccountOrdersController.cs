using System.Security.Claims;
using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

/// <summary>
/// Order history for logged-in storefront customers. Orders placed while logged in are linked
/// via Order.CustomerUserId (set at checkout from the caller's JWT). Older orders — placed before
/// that link existed, or where the identity wasn't attached for some reason — still fall back to
/// a case-insensitive match against the account's verified email, so nothing "disappears".
/// </summary>
[ApiController]
[Route("api/account/orders")]
[Authorize(Roles = "Customer")]
public class AccountOrdersController(BaglyDbContext db) : ControllerBase
{
    /// <summary>Common public Shiprocket tracking page (AWB path).</summary>
    private const string ShiprocketPublicTrackingBase = "https://shiprocket.co/tracking/";

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CustomerOrderDto>>> GetOrders(CancellationToken cancellationToken)
    {
        var (customerId, email) = await ResolveCustomerAsync(cancellationToken);
        if (customerId is null)
        {
            return Unauthorized(new { message = "Customer account not found." });
        }

        var orders = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.ShiprocketShipments)
            .Where(o => o.CustomerUserId == customerId || o.Email.ToLower() == email)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        var images = await GetProductImagesAsync(orders, cancellationToken);
        var historyByShipment = await GetTrackingHistoryAsync(orders.Select(o => o.Id).ToList(), cancellationToken);
        return Ok(orders.Select(o => MapOrder(o, images, historyByShipment)));
    }

    [HttpGet("{orderNumber}")]
    public async Task<ActionResult<CustomerOrderDto>> GetOrder(
        string orderNumber,
        CancellationToken cancellationToken)
    {
        var (customerId, email) = await ResolveCustomerAsync(cancellationToken);
        if (customerId is null)
        {
            return Unauthorized(new { message = "Customer account not found." });
        }

        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.ShiprocketShipments)
            .FirstOrDefaultAsync(
                o => o.OrderNumber == orderNumber &&
                     (o.CustomerUserId == customerId || o.Email.ToLower() == email),
                cancellationToken);

        if (order is null)
        {
            return NotFound(new { message = "Order not found." });
        }

        var images = await GetProductImagesAsync([order], cancellationToken);
        var historyByShipment = await GetTrackingHistoryAsync([order.Id], cancellationToken);
        return Ok(MapOrder(order, images, historyByShipment));
    }

    /// <summary>
    /// Customer tracking view: current status + timeline per shipment.
    /// Returns 404 when the order is not owned; empty shipments when shipping has not started.
    /// </summary>
    [HttpGet("{orderNumber}/track")]
    public async Task<ActionResult<CustomerOrderTrackDto>> GetOrderTrack(
        string orderNumber,
        CancellationToken cancellationToken)
    {
        var (customerId, email) = await ResolveCustomerAsync(cancellationToken);
        if (customerId is null)
        {
            return Unauthorized(new { message = "Customer account not found." });
        }

        var order = await db.Orders.AsNoTracking()
            .Include(o => o.ShiprocketShipments)
            .FirstOrDefaultAsync(
                o => o.OrderNumber == orderNumber &&
                     (o.CustomerUserId == customerId || o.Email.ToLower() == email),
                cancellationToken);

        if (order is null)
        {
            return NotFound(new { message = "Order not found." });
        }

        var historyByShipment = await GetTrackingHistoryAsync([order.Id], cancellationToken);
        var shipments = MapShipments(order.ShiprocketShipments, historyByShipment);
        return Ok(new CustomerOrderTrackDto(
            order.Id,
            order.OrderNumber,
            order.Status,
            order.CreatedAt,
            shipments.Any(s => s.CanTrack),
            shipments
        ));
    }

    /// <summary>Looks up the caller's CustomerUsers row so we filter orders by a trusted, current id + email.</summary>
    private async Task<(Guid? CustomerId, string? Email)> ResolveCustomerAsync(CancellationToken cancellationToken)
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(raw, out var customerId))
        {
            return (null, null);
        }

        var customer = await db.CustomerUsers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId && c.IsActive, cancellationToken);

        return customer is null
            ? (null, null)
            : (customer.Id, customer.Email.Trim().ToLowerInvariant());
    }

    private async Task<Dictionary<string, string>> GetProductImagesAsync(
        IReadOnlyList<Order> orders,
        CancellationToken cancellationToken)
    {
        var productIds = orders.SelectMany(o => o.Items).Select(i => i.ProductId).Distinct().ToList();
        if (productIds.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Image, cancellationToken);
    }

    private async Task<Dictionary<Guid, List<OrderShipmentTracking>>> GetTrackingHistoryAsync(
        IReadOnlyList<Guid> orderIds,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return new Dictionary<Guid, List<OrderShipmentTracking>>();
        }

        var rows = await db.OrderShipmentTrackings.AsNoTracking()
            .Where(t => orderIds.Contains(t.OrderId))
            .OrderBy(t => t.ChangedAtUtc)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(t => t.OrderShiprocketShipmentId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static CustomerOrderDto MapOrder(
        Order order,
        Dictionary<string, string> images,
        Dictionary<Guid, List<OrderShipmentTracking>> historyByShipment)
    {
        var shipments = MapShipments(order.ShiprocketShipments, historyByShipment);
        return new(
            order.Id,
            order.OrderNumber,
            order.Status,
            order.PaymentStatus,
            order.Currency,
            order.Subtotal,
            order.Shipping,
            order.Total,
            order.CreatedAt,
            order.Items.Select(i => new CustomerOrderItemDto(
                i.ProductId,
                i.ProductName,
                i.Color,
                i.UnitPrice,
                i.Quantity,
                i.UnitPrice * i.Quantity,
                images.TryGetValue(i.ProductId, out var image) ? image : null
            )).ToList(),
            shipments.Any(s => s.CanTrack),
            shipments
        );
    }

    private static IReadOnlyList<CustomerOrderShipmentDto> MapShipments(
        IEnumerable<OrderShiprocketShipment>? shipments,
        Dictionary<Guid, List<OrderShipmentTracking>> historyByShipment)
    {
        if (shipments is null)
        {
            return [];
        }

        return shipments
            .OrderBy(s => s.CreatedAt)
            .Select(s =>
            {
                var canTrack = IsTrackable(s);
                var history = historyByShipment.TryGetValue(s.Id, out var events)
                    ? events
                        .Select(e => new CustomerShipmentTrackingEventDto(e.Status, e.ChangedAtUtc))
                        .ToList()
                    : new List<CustomerShipmentTrackingEventDto>();

                // If history is empty but we have a current status, surface a single summary event.
                if (history.Count == 0 && !string.IsNullOrWhiteSpace(s.TrackingStatus))
                {
                    history.Add(new CustomerShipmentTrackingEventDto(
                        s.TrackingStatus!,
                        s.TrackingStatusUpdatedAt ?? s.PickupRequestedAt ?? s.UpdatedAt ?? s.CreatedAt));
                }

                var awb = string.IsNullOrWhiteSpace(s.AwbCode) ? null : s.AwbCode.Trim();
                return new CustomerOrderShipmentDto(
                    s.Id,
                    awb,
                    string.IsNullOrWhiteSpace(s.TrackingStatus) ? null : s.TrackingStatus,
                    s.TrackingStatusUpdatedAt,
                    string.IsNullOrWhiteSpace(s.CourierName) ? null : s.CourierName,
                    string.IsNullOrWhiteSpace(s.LabelUrl) ? null : s.LabelUrl,
                    canTrack,
                    awb is null ? null : ShiprocketPublicTrackingBase + Uri.EscapeDataString(awb),
                    history
                );
            })
            .ToList();
    }

    /// <summary>
    /// Tracking is available once AWB is assigned or pickup/tracking has started.
    /// </summary>
    private static bool IsTrackable(OrderShiprocketShipment s) =>
        !string.IsNullOrWhiteSpace(s.AwbCode) ||
        !string.IsNullOrWhiteSpace(s.TrackingStatus) ||
        s.PickupRequestedAt is not null;
}
