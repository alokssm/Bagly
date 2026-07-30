using System.Text.Json;
using Bagly.Api.Data;
using Bagly.Api.Models;

namespace Bagly.Api.Services;

public interface IAuditLogService
{
    Task LogAsync(
        string category,
        string action,
        string message,
        string level = "Information",
        string? actorEmail = null,
        string? entityType = null,
        string? entityId = null,
        object? details = null,
        string? ipAddress = null,
        string? requestPath = null,
        CancellationToken cancellationToken = default);
}

public class AuditLogService(BaglyDbContext db, ILogger<AuditLogService> logger) : IAuditLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public async Task LogAsync(
        string category,
        string action,
        string message,
        string level = "Information",
        string? actorEmail = null,
        string? entityType = null,
        string? entityId = null,
        object? details = null,
        string? ipAddress = null,
        string? requestPath = null,
        CancellationToken cancellationToken = default)
    {
        var detailsJson = details is null ? null : JsonSerializer.Serialize(details, JsonOptions);

        var entry = new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            Level = level,
            Category = category,
            Action = action,
            ActorEmail = actorEmail,
            EntityType = entityType,
            EntityId = entityId,
            Message = message,
            DetailsJson = detailsJson,
            IpAddress = ipAddress,
            RequestPath = requestPath,
        };

        db.AuditLogs.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["AuditCategory"] = category,
            ["AuditAction"] = action,
            ["ActorEmail"] = actorEmail,
            ["EntityType"] = entityType,
            ["EntityId"] = entityId,
            ["IpAddress"] = ipAddress,
            ["RequestPath"] = requestPath,
        }))
        {
            if (string.Equals(level, "Error", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(level, "Critical", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError("{Message} | Details={Details}", message, detailsJson);
            }
            else if (string.Equals(level, "Warning", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("{Message} | Details={Details}", message, detailsJson);
            }
            else
            {
                logger.LogInformation("{Message} | Details={Details}", message, detailsJson);
            }
        }
    }
}
