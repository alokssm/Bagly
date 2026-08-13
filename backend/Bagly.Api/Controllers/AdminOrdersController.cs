using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

/// <summary>Admin-only, paginated order search — the secure replacement for any ad-hoc order dumps.</summary>
[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = "Admin")]
public class AdminOrdersController(BaglyDbContext db) : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 50;

    [HttpGet]
    public async Task<ActionResult<AdminOrdersPagedResult>> GetOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > MaxPageSize) pageSize = DefaultPageSize;

        var query = db.Orders.AsNoTracking().AsQueryable();

        if (from is DateOnly fromDate)
        {
            query = query.Where(o => o.CreatedAt >= IstTime.ToUtc(fromDate));
        }

        if (to is DateOnly toDate)
        {
            var toExclusiveUtc = IstTime.ToUtc(toDate.AddDays(1));
            query = query.Where(o => o.CreatedAt < toExclusiveUtc);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(o =>
                o.OrderNumber.Contains(term) ||
                o.Email.Contains(term) ||
                o.FirstName.Contains(term) ||
                o.LastName.Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new AdminOrderListItemDto(
                o.Id,
                o.OrderNumber,
                (o.FirstName + " " + o.LastName).Trim(),
                o.Email,
                o.Status,
                o.PaymentStatus,
                o.PaymentProvider,
                o.Currency ?? "INR",
                o.Total,
                o.Items.Count,
                o.CreatedAt,
                o.Phone,
                o.ShiprocketOrderId,
                o.ShiprocketStatus))
            .ToListAsync(cancellationToken);

        var (todayStartUtc, todayEndUtc) = IstTime.TodayRangeUtc();
        var todayCount = await db.Orders.AsNoTracking()
            .CountAsync(o => o.CreatedAt >= todayStartUtc && o.CreatedAt < todayEndUtc, cancellationToken);

        return Ok(new AdminOrdersPagedResult(items, totalCount, page, pageSize, totalPages, todayCount));
    }

    /// <summary>Full order detail (incl. line items) for the "expand" row action on the admin orders table.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> GetOrder(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        return order is null
            ? NotFound(new { message = "Order not found." })
            : Ok(OrdersController.MapOrder(order));
    }
}
