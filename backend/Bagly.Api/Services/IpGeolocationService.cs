using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Bagly.Api.Options;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Services;

public record GeoLocation(string Country, string? Region, string? City);

public interface IIpGeolocationService
{
    /// <summary>Resolves a rough (country/region/city) location for a client IP. Never throws —
    /// falls back to "Local" for private/loopback addresses and "Unknown" on any lookup failure.</summary>
    Task<GeoLocation> ResolveAsync(string? ipAddress, CancellationToken cancellationToken);
}

/// <summary>
/// Free IP geolocation via ipwho.is (primary) with GeoJS fallback — both allow commercial use and
/// require no API key. Backed by an in-memory 24h cache keyed by IP so repeat visitors from the
/// same address never re-hit the API. Registered as a singleton so the cache survives across
/// requests; lookups use a hard 2s timeout so a slow/unreachable API never meaningfully delays
/// the hit beacon response.
/// </summary>
public sealed class IpGeolocationService(
    IHttpClientFactory httpClientFactory,
    IOptions<GeoIpOptions> options,
    ILogger<IpGeolocationService> logger) : IIpGeolocationService
{
    private const string IpWhoIsClientName = "GeoIpIpWhoIs";
    private const string GeoJsClientName = "GeoIpGeoJs";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(2);
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
        var useGeoJsFirst = string.Equals(
            options.Value.Provider?.Trim(),
            "geojs",
            StringComparison.OrdinalIgnoreCase);

        if (useGeoJsFirst)
        {
            var geoJs = await TryGeoJsAsync(ipAddress, cancellationToken);
            if (geoJs is not null)
            {
                return geoJs;
            }

            var ipWhoIs = await TryIpWhoIsAsync(ipAddress, cancellationToken);
            if (ipWhoIs is not null)
            {
                return ipWhoIs;
            }
        }
        else
        {
            var ipWhoIs = await TryIpWhoIsAsync(ipAddress, cancellationToken);
            if (ipWhoIs is not null)
            {
                return ipWhoIs;
            }

            var geoJs = await TryGeoJsAsync(ipAddress, cancellationToken);
            if (geoJs is not null)
            {
                return geoJs;
            }
        }

        return UnknownLocation;
    }

    private async Task<GeoLocation?> TryIpWhoIsAsync(string ipAddress, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(LookupTimeout);

            var client = httpClientFactory.CreateClient(IpWhoIsClientName);
            var payload = await client.GetFromJsonAsync<IpWhoIsResponse>(
                Uri.EscapeDataString(ipAddress),
                timeoutCts.Token);

            if (payload is null || !payload.Success || string.IsNullOrWhiteSpace(payload.Country))
            {
                return null;
            }

            return new GeoLocation(
                payload.Country.Trim(),
                string.IsNullOrWhiteSpace(payload.Region) ? null : payload.Region.Trim(),
                string.IsNullOrWhiteSpace(payload.City) ? null : payload.City.Trim());
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException or HttpRequestException)
        {
            logger.LogDebug(ex, "ipwho.is lookup timed out or failed for {IpAddress}.", ipAddress);
            return null;
        }
    }

    private async Task<GeoLocation?> TryGeoJsAsync(string ipAddress, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(LookupTimeout);

            var client = httpClientFactory.CreateClient(GeoJsClientName);
            var payload = await client.GetFromJsonAsync<GeoJsResponse>(
                $"v1/ip/geo/{Uri.EscapeDataString(ipAddress)}.json",
                timeoutCts.Token);

            var country = payload?.Country?.Trim();
            if (string.IsNullOrWhiteSpace(country))
            {
                country = payload?.CountryCode?.Trim();
            }

            if (string.IsNullOrWhiteSpace(country))
            {
                return null;
            }

            return new GeoLocation(
                country,
                string.IsNullOrWhiteSpace(payload?.Region) ? null : payload.Region.Trim(),
                string.IsNullOrWhiteSpace(payload?.City) ? null : payload.City.Trim());
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException or HttpRequestException)
        {
            logger.LogDebug(ex, "GeoJS lookup timed out or failed for {IpAddress}.", ipAddress);
            return null;
        }
    }

    private sealed class IpWhoIsResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("region")]
        public string? Region { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }
    }

    private sealed class GeoJsResponse
    {
        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("country_code")]
        public string? CountryCode { get; set; }

        [JsonPropertyName("region")]
        public string? Region { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }
    }
}
