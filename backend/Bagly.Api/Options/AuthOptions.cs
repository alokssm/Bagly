namespace Bagly.Api.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpiresMinutes { get; set; } = 480;
}

public class AdminOptions
{
    public const string SectionName = "Admin";
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Name { get; set; } = "Admin";

    /// <summary>Alternate config key for the order-notification recipient (fallback to
    /// <c>EmailOptions.AdminOrderNotify</c> when unset). See <c>EmailOptions.ResolveAdminOrderNotifyEmail</c>.</summary>
    public string? OrderNotifyEmail { get; set; }

    public bool IsPasswordConfigured =>
        !string.IsNullOrWhiteSpace(Password) &&
        !Password.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase);

    public string ResolveEmail() =>
        string.IsNullOrWhiteSpace(Email) ? "admin@bagly.store" : Email.Trim();

    public string ResolveName() =>
        string.IsNullOrWhiteSpace(Name) ? "Bagly Admin" : Name.Trim();

    public string ResolvePassword() =>
        IsPasswordConfigured ? Password : "Admin@123";
}
