namespace Bagly.Api.Models;

/// <summary>A single storefront page view, used to power the admin "Traffic" (locations)
/// analytics page. Privacy: <see cref="IpAddress"/> is truncated (last octet/segment zeroed)
/// before storage — never the raw client IP — and <see cref="UserAgent"/> is stored short.</summary>
public class SiteHit
{
    public long Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
    public string Country { get; set; } = "Unknown";
    public string? Region { get; set; }
    public string? City { get; set; }
    public string? UserAgent { get; set; }
    public string? SessionId { get; set; }
}
