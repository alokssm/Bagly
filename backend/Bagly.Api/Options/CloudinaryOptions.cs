namespace Bagly.Api.Options;

public class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";

    public string CloudName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;

    public static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase);

    public bool HasCloudName => !IsPlaceholder(CloudName);
    public bool HasApiKey => !IsPlaceholder(ApiKey);
    public bool HasApiSecret => !IsPlaceholder(ApiSecret);

    /// <summary>All three credentials are set to real values (not appsettings placeholders).</summary>
    public bool IsConfigured => HasCloudName && HasApiKey && HasApiSecret;
}
