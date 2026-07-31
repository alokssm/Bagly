namespace Bagly.Api.Options;

public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>When false, sending is disabled even if Host/FromAddress are set.</summary>
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "noreply@bagly.store";
    public string FromName { get; set; } = "Bagly";
    public bool UseSsl { get; set; } = true;

    public static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase);

    public bool HasSmtpHost => !IsPlaceholder(Host);
    public bool HasFromAddress => !IsPlaceholder(FromAddress);

    /// <summary>Host and FromAddress are set to real values (not appsettings placeholders).</summary>
    public bool IsConfigured => HasSmtpHost && HasFromAddress;

    /// <summary>Email will be sent when an order is confirmed.</summary>
    public bool WillSend => IsConfigured && Enabled;
}
