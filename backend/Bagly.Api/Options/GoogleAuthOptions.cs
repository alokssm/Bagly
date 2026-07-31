namespace Bagly.Api.Options;

public class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    /// <summary>Google OAuth Web Client ID used to validate ID tokens from Google Identity Services.</summary>
    public string ClientId { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !ClientId.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase);
}
