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
            .Where(o => o.CustomerUserId == customerId || o.Email.ToLower() == email)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        var images = await GetProductImagesAsync(orders, cancellationToken);
        return Ok(orders.Select(o => MapOrder(o, images)));
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
            .FirstOrDefaultAsync(
                o => o.OrderNumber == orderNumber &&
                     (o.CustomerUserId == customerId || o.Email.ToLower() == email),
                cancellationToken);

        if (order is null)
        {
            return NotFound(new { message = "Order not found." });
        }

        var images = await GetProductImagesAsync([order], cancellationToken);
        return Ok(MapOrder(order, images));
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

    private static CustomerOrderDto MapOrder(Order order, Dictionary<string, string> images) =>
        new(
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
            )).ToList()
        );
}
