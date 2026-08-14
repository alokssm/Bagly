using System.Text.Json;
using Bagly.Api.Data;
using Bagly.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Shipping;

public interface IShiprocketWebhookLogService
{
    Task PersistAsync(ShiprocketWebhookLog entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShiprocketWebhookLogDto>> ListAsync(
        int take = 50,
        CancellationToken cancellationToken = default);
}

public sealed class ShiprocketWebhookLogService(
    BaglyDbContext db,
    ILogger<ShiprocketWebhookLogService> logger) : IShiprocketWebhookLogService
{
    public const int MaxBodyChars = 50_000;
    public const int MaxHeadersChars = 8_000;
    public const int MaxPathChars = 500;
    public const int MaxErrorChars = 500;
    public const int MaxStatusChars = 50;

    private static readonly HashSet<string> SecretHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "x-api-key",
        "api-key",
        "x-shiprocket-webhook-secret",
        "x-webhook-secret",
        "webhook-secret",
        "x-api-secret",
        "cookie",
        "set-cookie",
    };

    public async Task PersistAsync(ShiprocketWebhookLog entry, CancellationToken cancellationToken = default)
    {
        try
        {
            entry.HttpMethod = Truncate(entry.HttpMethod, 10) ?? "POST";
            entry.Path = Truncate(entry.Path, MaxPathChars) ?? "/api/webhooks/shiprocket";
            entry.HeadersJson = Truncate(entry.HeadersJson, MaxHeadersChars);
            entry.RequestBody = TruncateBody(entry.RequestBody);
            entry.ResponseBody = TruncateBody(entry.ResponseBody);
            entry.ErrorMessage = Truncate(entry.ErrorMessage, MaxErrorChars);
            entry.MappedStatus = Truncate(entry.MappedStatus, MaxStatusChars);

            db.ShiprocketWebhookLogs.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist Shiprocket webhook log.");
        }
    }

    public async Task<IReadOnlyList<ShiprocketWebhookLogDto>> ListAsync(
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        return await db.ShiprocketWebhookLogs.AsNoTracking()
            .OrderByDescending(x => x.ReceivedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .Select(x => new ShiprocketWebhookLogDto(
                x.Id,
                x.ReceivedAtUtc,
                x.HttpMethod,
                x.Path,
                x.HeadersJson,
                x.RequestBody,
                x.ResponseStatusCode,
                x.ResponseBody,
                x.ProcessedOk,
                x.ErrorMessage,
                x.MatchedOrderId,
                x.MatchedShipmentId,
                x.MappedStatus))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Serialize headers; secret values are masked (key names kept).</summary>
    public static string? BuildHeadersJson(IHeaderDictionary headers)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            var name = header.Key;
            var raw = header.Value.ToString() ?? string.Empty;
            map[name] = IsSecretHeader(name) ? MaskSecret(raw) : Truncate(raw, 500) ?? string.Empty;
        }

        try
        {
            return Truncate(JsonSerializer.Serialize(map), MaxHeadersChars);
        }
        catch
        {
            return null;
        }
    }

    public static string TruncateBody(string? value, int max = MaxBodyChars)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        return value.Length <= max ? value : value[..max] + "…";
    }

    private static bool IsSecretHeader(string name) => SecretHeaderNames.Contains(name);

    /// <summary>Keep key visible; mask value (first/last 2 chars when long enough).</summary>
    internal static string MaskSecret(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var trimmed = value.Trim();
        if (trimmed.Length <= 4) return "****";
        if (trimmed.Length <= 8) return trimmed[..1] + "****" + trimmed[^1..];
        return trimmed[..2] + "****" + trimmed[^2..];
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max];
    }
}
