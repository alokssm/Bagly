namespace Bagly.Api.Options;

public class RazorpayOptions
{
    public const string SectionName = "Razorpay";

    public string KeyId { get; set; } = string.Empty;
    public string KeySecret { get; set; } = string.Empty;
    public string Currency { get; set; } = "INR";

    /// <summary>
    /// Catalog prices are stored in USD. Convert to INR for Razorpay charges.
    /// </summary>
    public decimal UsdToInrRate { get; set; } = 83m;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(KeyId) &&
        !string.IsNullOrWhiteSpace(KeySecret) &&
        !KeyId.Contains("REPLACE", StringComparison.OrdinalIgnoreCase) &&
        !KeySecret.Contains("REPLACE", StringComparison.OrdinalIgnoreCase);
}
