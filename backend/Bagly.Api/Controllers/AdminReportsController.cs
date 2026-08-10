using Bagly.Api.Data;
using Bagly.Api.Models.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

[ApiController]
[Route("api/admin/reports")]
[Authorize(Roles = "Admin")]
public class AdminReportsController(BaglyDbContext db) : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    [HttpGet("summary")]
    public async Task<ActionResult<ReportSummaryDto>> GetSummary(CancellationToken cancellationToken)
    {
        var auditCount = await db.AuditLogs.CountAsync(cancellationToken);
        var systemCount = await db.SystemLogs.CountAsync(cancellationToken);
        var errorCount = await db.AuditLogs.CountAsync(x => x.Level == "Error" || x.Level == "Fatal", cancellationToken)
            + await db.SystemLogs.CountAsync(x => x.Level == "Error" || x.Level == "Fatal", cancellationToken);
        var warningCount = await db.AuditLogs.CountAsync(x => x.Level == "Warning", cancellationToken)
            + await db.SystemLogs.CountAsync(x => x.Level == "Warning", cancellationToken);

        // Project GroupBy aggregates into an anonymous type and map to the record DTO after
        // materialization — EF Core cannot reliably translate GroupBy→record constructors on
        // Npgsql (same class of bug as AdminAnalyticsController).
        var byCategory = (await db.AuditLogs
            .GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(cancellationToken))
            .Select(x => new CategoryCountDto(x.Category, x.Count))
            .ToList();

        return Ok(new ReportSummaryDto(auditCount, systemCount, errorCount, warningCount, byCategory));
    }

    [HttpGet("audit-logs")]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> GetAuditLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? category = null,
        [FromQuery] string? level = null,
        [FromQuery] string? action = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePaging(page, pageSize);

        var query = db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(x => x.Category == category.Trim());

        if (!string.IsNullOrWhiteSpace(level))
            query = query.Where(x => x.Level == level.Trim());

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(x => x.Action == action.Trim());

        if (from.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(from.Value.ToUniversalTime(), DateTimeKind.Utc);
            query = query.Where(x => x.TimestampUtc >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(to.Value.ToUniversalTime(), DateTimeKind.Utc);
            query = query.Where(x => x.TimestampUtc <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.Message.Contains(term) ||
                (x.ActorEmail != null && x.ActorEmail.Contains(term)) ||
                (x.EntityId != null && x.EntityId.Contains(term)) ||
                (x.RequestPath != null && x.RequestPath.Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = (await query
            .OrderByDescending(x => x.TimestampUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.TimestampUtc,
                x.Level,
                x.Category,
                x.Action,
                x.ActorEmail,
                x.EntityType,
                x.EntityId,
                x.Message,
                x.DetailsJson,
                x.IpAddress,
                x.RequestPath,
            })
            .ToListAsync(cancellationToken))
            .Select(x => new AuditLogDto(
                x.Id,
                x.TimestampUtc,
                x.Level,
                x.Category,
                x.Action,
                x.ActorEmail,
                x.EntityType,
                x.EntityId,
                x.Message,
                x.DetailsJson,
                x.IpAddress,
                x.RequestPath))
            .ToList();

        return Ok(new PagedResult<AuditLogDto>(items, page, pageSize, totalCount, totalPages));
    }

    [HttpGet("system-logs")]
    public async Task<ActionResult<PagedResult<SystemLogDto>>> GetSystemLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? level = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePaging(page, pageSize);

        var query = db.SystemLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(level))
            query = query.Where(x => x.Level == level.Trim());

        if (from.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(from.Value.ToUniversalTime(), DateTimeKind.Utc);
            query = query.Where(x => x.TimeStamp >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(to.Value.ToUniversalTime(), DateTimeKind.Utc);
            query = query.Where(x => x.TimeStamp <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                (x.Message != null && x.Message.Contains(term)) ||
                (x.Exception != null && x.Exception.Contains(term)) ||
                (x.ActorEmail != null && x.ActorEmail.Contains(term)) ||
                (x.RequestPath != null && x.RequestPath.Contains(term)) ||
                (x.AuditAction != null && x.AuditAction.Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = (await query
            .OrderByDescending(x => x.TimeStamp)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.TimeStamp,
                x.Level,
                x.Message,
                x.Exception,
                x.RequestPath,
                x.ActorEmail,
                x.AuditCategory,
                x.AuditAction,
            })
            .ToListAsync(cancellationToken))
            .Select(x => new SystemLogDto(
                x.Id,
                x.TimeStamp,
                x.Level,
                x.Message,
                x.Exception,
                x.RequestPath,
                x.ActorEmail,
                x.AuditCategory,
                x.AuditAction))
            .ToList();

        return Ok(new PagedResult<SystemLogDto>(items, page, pageSize, totalCount, totalPages));
    }

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (page, pageSize);
    }
}
