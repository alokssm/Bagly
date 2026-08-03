using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

[ApiController]
[Route("api/admin/analytics")]
[Authorize(Roles = "Admin")]
public class AdminAnalyticsController(BaglyDbContext db) : ControllerBase
{
    /// <summary>An order is counted as a completed sale once it reaches Status == "Confirmed" —
    /// this covers both Razorpay-paid (India) and NotRequired (non-India) successful checkouts,
    /// and excludes AwaitingPayment/PaymentFailed/OutOfStock orders.</summary>
    private const string SuccessStatus = "Confirmed";

    [HttpGet]
    public async Task<ActionResult<AdminAnalyticsDto>> GetAnalytics(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
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

        var totalOrders = await query.CountAsync(cancellationToken);

        var successfulQuery = query.Where(o => o.Status == SuccessStatus);
        var successfulCount = await successfulQuery.CountAsync(cancellationToken);
        var totalRevenue = successfulCount == 0
            ? 0m
            : await successfulQuery.SumAsync(o => o.Total, cancellationToken);
        var averageOrderValue = successfulCount == 0 ? 0m : totalRevenue / successfulCount;

        var ordersByStatus = await query
            .GroupBy(o => o.Status)
            .Select(g => new OrderStatusCountDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToListAsync(cancellationToken);

        var topProducts = await (
            from oi in db.OrderItems.AsNoTracking()
            join o in successfulQuery on oi.OrderId equals o.Id
            group oi by new { oi.ProductId, oi.ProductName } into g
            orderby g.Sum(x => x.Quantity) descending
            select new TopProductSoldDto(
                g.Key.ProductId,
                g.Key.ProductName,
                g.Sum(x => x.Quantity),
                g.Sum(x => x.UnitPrice * x.Quantity)))
            .Take(10)
            .ToListAsync(cancellationToken);

        // Today/this-week/this-month are fixed "now" anchors in IST — independent of the from/to
        // filter above, which instead scopes the totals, breakdown, and top-products figures.
        var (todayStartUtc, todayEndUtc) = IstTime.TodayRangeUtc();
        var (weekStartUtc, weekEndUtc) = IstTime.ThisWeekRangeUtc();
        var (monthStartUtc, monthEndUtc) = IstTime.ThisMonthRangeUtc();

        var ordersToday = await db.Orders.AsNoTracking()
            .CountAsync(o => o.CreatedAt >= todayStartUtc && o.CreatedAt < todayEndUtc, cancellationToken);
        var ordersThisWeek = await db.Orders.AsNoTracking()
            .CountAsync(o => o.CreatedAt >= weekStartUtc && o.CreatedAt < weekEndUtc, cancellationToken);
        var ordersThisMonth = await db.Orders.AsNoTracking()
            .CountAsync(o => o.CreatedAt >= monthStartUtc && o.CreatedAt < monthEndUtc, cancellationToken);

        return Ok(new AdminAnalyticsDto(
            from,
            to,
            totalOrders,
            totalRevenue,
            averageOrderValue,
            ordersToday,
            ordersThisWeek,
            ordersThisMonth,
            ordersByStatus,
            topProducts));
    }
}
