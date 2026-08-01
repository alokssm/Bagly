namespace Bagly.Api.Options;

public class RazorpayOptions
{
    public const string SectionName = "Razorpay";

    public string KeyId { get; set; } = string.Empty;
    public string KeySecret { get; set; } = string.Empty;
    public string Currency { get; set; } = "INR";

    /// <summary>
    /// Unused. Catalog prices are stored directly in INR, so no USD→INR conversion happens
    /// anywhere in the payment path. Kept only so existing config/env vars (e.g. Render)
    /// don't fail to bind; safe to remove from config whenever convenient.
    /// </summary>
    public decimal UsdToInrRate { get; set; } = 83m;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(KeyId) &&
        !string.IsNullOrWhiteSpace(KeySecret) &&
        !KeyId.Contains("REPLACE", StringComparison.OrdinalIgnoreCase) &&
        !KeySecret.Contains("REPLACE", StringComparison.OrdinalIgnoreCase);
}
