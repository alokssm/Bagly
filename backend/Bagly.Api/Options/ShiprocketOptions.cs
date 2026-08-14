namespace Bagly.Api.Options;

public class ShiprocketOptions
{
    public const string SectionName = "Shiprocket";

    public bool Enabled { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>Default pickup nickname when a product has no <c>ShiprocketPickupLocation</c>.</summary>
    public string PickupLocation { get; set; } = string.Empty;

    /// <summary>
    /// Optional comma-separated nicknames for seller/admin UI (e.g. <c>home,work</c>).
    /// Does not auto-create addresses in Shiprocket.
    /// </summary>
    public string PickupLocations { get; set; } = "home,work";

    public string BaseUrl { get; set; } = "https://apiv2.shiprocket.in";

    /// <summary>
    /// Optional shared secret for shipping webhook POSTs
    /// (<c>/api/webhooks/shipping-status</c> or legacy <c>/api/webhooks/shiprocket</c>).
    /// When set (and not a SET_VIA_ENV placeholder), non-empty webhook payloads must send the same value in
    /// <c>x-api-key</c>, <c>X-Shiprocket-Webhook-Secret</c>, or <c>Authorization: Bearer …</c>
    /// (Shiprocket panel “API key” / security token). Empty panel probes are accepted without the secret.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>True when <see cref="WebhookSecret"/> is configured and usable.</summary>
    public bool HasWebhookSecret =>
        !string.IsNullOrWhiteSpace(WebhookSecret) &&
        !IsMissingCredential(WebhookSecret);

    /// <summary>Configured UI nicknames (trimmed, non-empty), plus default <see cref="PickupLocation"/> when set.</summary>
    public IReadOnlyList<string> GetPickupLocationChoices()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in (PickupLocations ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IsPlaceholderPickup(part))
            {
                set.Add(part);
            }
        }

        if (!IsPlaceholderPickup(PickupLocation))
        {
            set.Add(PickupLocation.Trim());
        }

        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public double DefaultLength { get; set; } = 10;

    public double DefaultBreadth { get; set; } = 15;

    public double DefaultHeight { get; set; } = 20;

    public double DefaultWeightKg { get; set; } = 0.5;

    /// <summary>
    /// When true, checkout awaits Shiprocket create (slower; useful for debugging).
    /// Default false uses the background dispatcher.
    /// </summary>
    public bool SyncCreateOnCheckout { get; set; }

    public bool IsConfigured =>
        Enabled &&
        !IsMissingCredential(Email) &&
        !IsMissingCredential(Password) &&
        !IsPlaceholderPickup(PickupLocation);

    public static bool IsMissingCredential(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// "test" was a common Render placeholder and never matches a real Shiprocket nickname.
    /// </summary>
    public static bool IsPlaceholderPickup(string? pickup) =>
        string.IsNullOrWhiteSpace(pickup) ||
        pickup.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(pickup.Trim(), "test", StringComparison.OrdinalIgnoreCase);
}
