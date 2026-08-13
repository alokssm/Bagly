namespace Bagly.Api.Options;

public class ShiprocketOptions
{
    public const string SectionName = "Shiprocket";

    public bool Enabled { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>Pickup location nickname as configured in the Shiprocket panel (single warehouse for v1).</summary>
    public string PickupLocation { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://apiv2.shiprocket.in";

    public double DefaultLength { get; set; } = 10;

    public double DefaultBreadth { get; set; } = 15;

    public double DefaultHeight { get; set; } = 20;

    public double DefaultWeightKg { get; set; } = 0.5;

    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(Email) &&
        !Email.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !Password.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(PickupLocation) &&
        !PickupLocation.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase);
}
