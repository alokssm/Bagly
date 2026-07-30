namespace Bagly.Api.Models.Dtos;

public record AuditLogDto(
    long Id,
    DateTime TimestampUtc,
    string Level,
    string Category,
    string Action,
    string? ActorEmail,
    string? EntityType,
    string? EntityId,
    string Message,
    string? DetailsJson,
    string? IpAddress,
    string? RequestPath);

public record SystemLogDto(
    int Id,
    DateTime TimeStamp,
    string? Level,
    string? Message,
    string? Exception,
    string? RequestPath,
    string? ActorEmail,
    string? AuditCategory,
    string? AuditAction);

public record ReportSummaryDto(
    int AuditLogCount,
    int SystemLogCount,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<CategoryCountDto> ByCategory);

public record CategoryCountDto(string Category, int Count);
