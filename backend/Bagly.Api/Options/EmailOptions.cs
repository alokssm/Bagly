namespace Bagly.Api.Options;

public enum EmailProvider
{
    Smtp,
    SendGrid,
}

public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>When false, sending is disabled even if Host/FromAddress are set.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Smtp (default) or SendGrid (HTTPS API — works on Render free tier).</summary>
    public string Provider { get; set; } = "Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>SendGrid REST API key (SG.xxx). Falls back to Password when Provider is SendGrid.</summary>
    public string? SendGridApiKey { get; set; }

    public string FromAddress { get; set; } = "noreply@bagly.store";
    public string FromName { get; set; } = "Bagly";
    public bool UseSsl { get; set; } = true;

    public static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase);

    public bool HasSmtpHost => !IsPlaceholder(Host);
    public bool HasFromAddress => !IsPlaceholder(FromAddress);

    public EmailProvider ResolvedProvider =>
        string.Equals(Provider, "SendGrid", StringComparison.OrdinalIgnoreCase)
            ? EmailProvider.SendGrid
            : EmailProvider.Smtp;

    public bool HasSendGridApiKey =>
        !IsPlaceholder(SendGridApiKey) ||
        (ResolvedProvider == EmailProvider.SendGrid && !IsPlaceholder(Password));

    public string? ResolveSendGridApiKey()
    {
        if (!IsPlaceholder(SendGridApiKey))
        {
            return SendGridApiKey!.Trim();
        }

        if (ResolvedProvider == EmailProvider.SendGrid && !IsPlaceholder(Password))
        {
            return Password!.Trim();
        }

        return null;
    }

    /// <summary>Host and FromAddress are set to real values (not appsettings placeholders).</summary>
    public bool IsConfigured => ResolvedProvider switch
    {
        EmailProvider.SendGrid => HasFromAddress && HasSendGridApiKey,
        _ => HasSmtpHost && HasFromAddress,
    };

    /// <summary>Email will be sent when an order is confirmed.</summary>
    public bool WillSend => IsConfigured && Enabled;

    public bool UsesSmtpOnRenderFreeTier =>
        ResolvedProvider == EmailProvider.Smtp && WillSend;
}

