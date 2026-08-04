using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Extensions;
using Bagly.Api.Models;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Bagly.Api.Controllers;

/// <summary>Public storefront traffic beacon — no auth required. The frontend fires one call per
/// page navigation and ignores the response entirely, so every code path here degrades quietly
/// (rate limit, geolocation timeout, DB hiccup) rather than surfacing an error to the visitor.</summary>
[ApiController]
[Route("api/analytics")]
public class AnalyticsController(
    BaglyDbContext db,
    IIpGeolocationService geolocation,
    ISiteHitRateLimiter rateLimiter,
    ILogger<AnalyticsController> logger) : ControllerBase
{
    private const int MaxPathLength = 500;
    private const int MaxUserAgentLength = 300;
    private const int MaxSessionIdLength = 100;

    [HttpPost("hit")]
    public async Task<IActionResult> RecordHit([FromBody] SiteHitRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Path))
        {
            return Ok();
        }

        var ip = HttpContext.GetClientIp();
        if (!string.IsNullOrWhiteSpace(ip) && !rateLimiter.TryConsume(ip))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var (country, region, city) = await ResolveLocationAsync(ip, cancellationToken);

        var userAgent = Request.Headers.UserAgent.ToString();
        if (userAgent.Length > MaxUserAgentLength)
        {
            userAgent = userAgent[..MaxUserAgentLength];
        }

        var path = request.Path.Trim();
        if (path.Length > MaxPathLength)
        {
            path = path[..MaxPathLength];
        }

        var sessionId = request.SessionId?.Trim();
        if (!string.IsNullOrEmpty(sessionId) && sessionId.Length > MaxSessionIdLength)
        {
            sessionId = sessionId[..MaxSessionIdLength];
        }

        var hit = new SiteHit
        {
            Path = path,
            OccurredAtUtc = DateTime.UtcNow,
            IpAddress = IpPrivacy.Mask(ip),
            Country = country,
            Region = region,
            City = city,
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            SessionId = string.IsNullOrEmpty(sessionId) ? null : sessionId,
        };

        try
        {
            db.SiteHits.Add(hit);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist site hit for path {Path}.", hit.Path);
        }

        return Ok();
    }

    /// <summary>Prefers the CDN-provided country header (instant, no external call) and only
    /// falls back to ipwho.is / GeoJS — which also give region/city — when it's absent.</summary>
    private async Task<(string Country, string? Region, string? City)> ResolveLocationAsync(
        string? ip,
        CancellationToken cancellationToken)
    {
        var cfCountry = Request.Headers["CF-IPCountry"].ToString();
        if (!string.IsNullOrWhiteSpace(cfCountry) && !string.Equals(cfCountry, "XX", StringComparison.OrdinalIgnoreCase))
        {
            return (cfCountry.Trim(), null, null);
        }

        var location = await geolocation.ResolveAsync(ip, cancellationToken);
        return (location.Country, location.Region, location.City);
    }
}
