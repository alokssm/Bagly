using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Bagly.Api.Services;

public record GeoLocation(string Country, string? Region, string? City);

public interface IIpGeolocationService
{
    /// <summary>Resolves a rough (country/region/city) location for a client IP. Never throws —
    /// falls back to "Local" for private/loopback addresses and "Unknown" on any lookup failure.</summary>
    Task<GeoLocation> ResolveAsync(string? ipAddress, CancellationToken cancellationToken);
}

/// <summary>
/// Free-tier IP geolocation via ip-api.com (45 requests/min limit on the free plan), backed by an
/// in-memory 24h cache keyed by IP so repeat visitors from the same address never re-hit the API.
/// Registered as a singleton so the cache survives across requests; ip-api.com is called with a
/// hard 2s timeout so a slow/unreachable API never meaningfully delays the hit beacon response.
/// </summary>
public sealed class IpGeolocationService(
    IHttpClientFactory httpClientFactory,
    ILogger<IpGeolocationService> logger) : IIpGeolocationService
{
    private const string HttpClientName = "IpGeolocation";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly GeoLocation LocalLocation = new("Local", null, null);
    private static readonly GeoLocation UnknownLocation = new("Unknown", null, null);

    private readonly ConcurrentDictionary<string, (GeoLocation Location, DateTime ExpiresAtUtc)> _cache = new();

    public async Task<GeoLocation> ResolveAsync(string? ipAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress, out var parsed))
        {
            return LocalLocation;
        }

        if (parsed.IsPrivateOrLocal())
        {
            return LocalLocation;
        }

        var now = DateTime.UtcNow;
        if (_cache.TryGetValue(ipAddress, out var cached) && cached.ExpiresAtUtc > now)
        {
            return cached.Location;
        }

        var location = await FetchAsync(ipAddress, cancellationToken);
        _cache[ipAddress] = (location, now.Add(CacheTtl));
        return location;
    }

    private async Task<GeoLocation> FetchAsync(string ipAddress, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

            var client = httpClientFactory.CreateClient(HttpClientName);
            var payload = await client.GetFromJsonAsync<IpApiResponse>(
                $"json/{Uri.EscapeDataString(ipAddress)}?fields=status,country,regionName,city",
                timeoutCts.Token);

            if (payload is null || !string.Equals(payload.Status, "success", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(payload.Country))
            {
                return UnknownLocation;
            }

            return new GeoLocation(
                payload.Country.Trim(),
                string.IsNullOrWhiteSpace(payload.RegionName) ? null : payload.RegionName.Trim(),
                string.IsNullOrWhiteSpace(payload.City) ? null : payload.City.Trim());
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException or HttpRequestException)
        {
            logger.LogWarning(ex, "IP geolocation lookup timed out or failed.");
            return UnknownLocation;
        }
    }

    private sealed class IpApiResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("regionName")]
        public string? RegionName { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }
    }
}
