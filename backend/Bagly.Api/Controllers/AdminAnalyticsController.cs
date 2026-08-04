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

        // NOTE: project GroupBy aggregates into an anonymous type and keep GROUP BY / ORDER BY /
        // TOP in SQL, but defer constructing the record DTO until *after* ToListAsync. EF Core 8's
        // SQL Server translator cannot reliably translate `GroupBy(...).Select(g => new
        // SomeRecord(g.Key, g.Count()))` — it throws "The LINQ expression ... could not be
        // translated" at request time (caught by ExceptionLoggingMiddleware and surfaced to the
        // admin UI as the generic "An unexpected error occurred." message). Mapping to the DTO
        // client-side after materializing the anonymous-type rows sidesteps that limitation.
        var ordersByStatus = (await query
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(cancellationToken))
            .Select(x => new OrderStatusCountDto(x.Status, x.Count))
            .ToList();

        var topProducts = (await (
            from oi in db.OrderItems.AsNoTracking()
            join o in successfulQuery on oi.OrderId equals o.Id
            group oi by new { oi.ProductId, oi.ProductName } into g
            orderby g.Sum(x => x.Quantity) descending
            select new
            {
                g.Key.ProductId,
                g.Key.ProductName,
                QuantitySold = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.UnitPrice * x.Quantity),
            })
            .Take(10)
            .ToListAsync(cancellationToken))
            .Select(x => new TopProductSoldDto(x.ProductId, x.ProductName, x.QuantitySold, x.Revenue))
            .ToList();

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

    [HttpGet("locations")]
    public async Task<ActionResult<AdminLocationsAnalyticsDto>> GetLocationAnalytics(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.SiteHits.AsNoTracking().AsQueryable();

        if (from is DateOnly fromDate)
        {
            query = query.Where(h => h.OccurredAtUtc >= IstTime.ToUtc(fromDate));
        }

        if (to is DateOnly toDate)
        {
            var toExclusiveUtc = IstTime.ToUtc(toDate.AddDays(1));
            query = query.Where(h => h.OccurredAtUtc < toExclusiveUtc);
        }

        var totalHits = await query.CountAsync(cancellationToken);

        var uniqueSessions = await query
            .Where(h => h.SessionId != null)
            .Select(h => h.SessionId)
            .Distinct()
            .CountAsync(cancellationToken);

        // Two narrow, separately-materialized queries (hit counts per country, then session ids
        // per country) instead of one GroupBy with a nested Distinct/Count — see the note on
        // GroupBy translation above; the same SQL Server LINQ translator limitation applies here.
        var hitsByCountry = await query
            .GroupBy(h => h.Country)
            .Select(g => new { Country = g.Key, Hits = g.Count() })
            .OrderByDescending(x => x.Hits)
            .Take(50)
            .ToListAsync(cancellationToken);

        var sessionPairs = await query
            .Where(h => h.SessionId != null)
            .Select(h => new { h.Country, h.SessionId })
            .ToListAsync(cancellationToken);

        var uniqueSessionsByCountry = sessionPairs
            .GroupBy(x => x.Country, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.SessionId).Distinct().Count(),
                StringComparer.OrdinalIgnoreCase);

        var locations = hitsByCountry
            .Select(x => new LocationHitDto(
                x.Country,
                x.Hits,
                uniqueSessionsByCountry.GetValueOrDefault(x.Country),
                totalHits == 0 ? 0 : Math.Round(x.Hits * 100.0 / totalHits, 1)))
            .ToList();

        return Ok(new AdminLocationsAnalyticsDto(from, to, totalHits, uniqueSessions, locations));
    }
}
