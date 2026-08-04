namespace Bagly.Api.Options;

public class GeoIpOptions
{
    public const string SectionName = "GeoIp";

    /// <summary>Primary geolocation provider: "ipwhois" (default) or "geojs". Both are free,
    /// keyless, and allow commercial use; the non-selected provider is used as fallback.</summary>
    public string Provider { get; set; } = "ipwhois";
}
