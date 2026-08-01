namespace Bagly.Api.Options;

/// <summary>Where the public storefront is hosted, used to build links in restock/notification emails.</summary>
public class StorefrontOptions
{
    public const string SectionName = "Storefront";

    /// <summary>e.g. https://bagly-one.vercel.app. Falls back to the first Cors:AllowedOrigins entry when unset.</summary>
    public string? BaseUrl { get; set; }
}
