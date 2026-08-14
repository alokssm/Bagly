using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Bagly.Api.Data;
using Bagly.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Shipping;

public interface IShiprocketApiLogService
{
    Task LogAsync(
        string action,
        string httpMethod,
        string url,
        string? requestJson,
        int? responseStatus = null,
        string? responseJson = null,
        Guid? orderId = null,
        Guid? shipmentId = null,
        string? adminEmail = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShiprocketApiLogDto>> ListAsync(
        Guid? orderId,
        Guid? shipmentId,
        int take = 50,
        CancellationToken cancellationToken = default);
}

public sealed class ShiprocketApiLogService(
    BaglyDbContext db,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ShiprocketApiLogService> logger) : IShiprocketApiLogService
{
    private const int MaxJsonChars = 16_000;

    private static readonly Regex BearerInText = new(
        @"Bearer\s+[A-Za-z0-9\-._~+/]+=*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task LogAsync(
        string action,
        string httpMethod,
        string url,
        string? requestJson,
        int? responseStatus = null,
        string? responseJson = null,
        Guid? orderId = null,
        Guid? shipmentId = null,
        string? adminEmail = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            adminEmail ??= ResolveAdminEmail();

            var entry = new ShiprocketApiLog
            {
                OrderId = orderId,
                ShipmentId = shipmentId,
                Action = Truncate(action, 80) ?? "Unknown",
                HttpMethod = Truncate(httpMethod, 10) ?? "GET",
                Url = Truncate(url, 500) ?? string.Empty,
                RequestJson = RedactAndTruncate(requestJson),
                ResponseStatus = responseStatus,
                ResponseJson = RedactAndTruncate(responseJson),
                CreatedAtUtc = DateTime.UtcNow,
                AdminEmail = Truncate(adminEmail, 256),
            };

            db.ShiprocketApiLogs.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist Shiprocket API log for action {Action}.", action);
        }
    }

    public async Task<IReadOnlyList<ShiprocketApiLogDto>> ListAsync(
        Guid? orderId,
        Guid? shipmentId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var query = db.ShiprocketApiLogs.AsNoTracking().AsQueryable();
        if (orderId is Guid oid)
        {
            query = query.Where(x => x.OrderId == oid);
        }

        if (shipmentId is Guid sid)
        {
            query = query.Where(x => x.ShipmentId == sid);
        }

        return await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .Select(x => new ShiprocketApiLogDto(
                x.Id,
                x.OrderId,
                x.ShipmentId,
                x.Action,
                x.HttpMethod,
                x.Url,
                x.RequestJson,
                x.ResponseStatus,
                x.ResponseJson,
                x.CreatedAtUtc,
                x.AdminEmail))
            .ToListAsync(cancellationToken);
    }

    private string? ResolveAdminEmail()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null) return null;
        return user.FindFirstValue(ClaimTypes.Email)
               ?? user.FindFirstValue("email")
               ?? user.Identity?.Name;
    }

    internal static string? RedactAndTruncate(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        var redacted = RedactSecrets(raw);
        return redacted.Length <= MaxJsonChars
            ? redacted
            : redacted[..MaxJsonChars] + "…";
    }

    internal static string RedactSecrets(string raw)
    {
        var trimmed = raw.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                var node = JsonNode.Parse(raw);
                if (node is not null)
                {
                    RedactNode(node);
                    return node.ToJsonString();
                }
            }
            catch (JsonException)
            {
                // fall through to regex
            }
        }

        return BearerInText.Replace(raw, "Bearer [REDACTED]");
    }

    private static void RedactNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var prop in obj.ToList())
            {
                var name = prop.Key;
                if (IsSecretKey(name))
                {
                    obj[name] = "[REDACTED]";
                    continue;
                }

                if (prop.Value is not null)
                {
                    RedactNode(prop.Value);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not null) RedactNode(item);
            }
        }
    }

    private static bool IsSecretKey(string name) =>
        name.Equals("password", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("token", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("access_token", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("auth_token", StringComparison.OrdinalIgnoreCase);

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max];
    }
}
