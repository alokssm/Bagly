namespace Bagly.Api.Options;

public enum EmailProvider
{
    Smtp,
    SendGrid,
    Resend,
}

public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>When false, sending is disabled even if Host/FromAddress are set.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Smtp (default), SendGrid, or Resend (HTTPS APIs — work on Render free tier).</summary>
    public string Provider { get; set; } = "Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>SendGrid REST API key (SG.xxx). Falls back to Password when Provider is SendGrid.</summary>
    public string? SendGridApiKey { get; set; }

    /// <summary>Resend REST API key (re_xxx).</summary>
    public string? ResendApiKey { get; set; }

    public string FromAddress { get; set; } = "noreply@bagly.co.in";
    public string FromName { get; set; } = "Bagly";
    public bool UseSsl { get; set; } = true;

    /// <summary>Admin mailbox that gets a copy of every successfully placed order. Overridable via
    /// env var <c>Email__AdminOrderNotify</c> (preferred) or <c>Admin__OrderNotifyEmail</c>.</summary>
    public string AdminOrderNotify { get; set; } = "alok73772@gmail.com";

    public static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase);

    public bool HasSmtpHost => !IsPlaceholder(Host);
    public bool HasFromAddress => !IsPlaceholder(FromAddress);

    public EmailProvider ResolvedProvider =>
        string.Equals(Provider, "SendGrid", StringComparison.OrdinalIgnoreCase)
            ? EmailProvider.SendGrid
            : string.Equals(Provider, "Resend", StringComparison.OrdinalIgnoreCase)
                ? EmailProvider.Resend
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

    public bool HasResendApiKey => !IsPlaceholder(ResendApiKey);

    public string? ResolveResendApiKey() =>
        HasResendApiKey ? ResendApiKey!.Trim() : null;

    /// <summary>Host and FromAddress are set to real values (not appsettings placeholders).</summary>
    public bool IsConfigured => ResolvedProvider switch
    {
        EmailProvider.SendGrid => HasFromAddress && HasSendGridApiKey,
        EmailProvider.Resend => HasFromAddress && HasResendApiKey,
        _ => HasSmtpHost && HasFromAddress,
    };

    /// <summary>Email will be sent when an order is confirmed.</summary>
    public bool WillSend => IsConfigured && Enabled;

    public bool UsesSmtpOnRenderFreeTier =>
        ResolvedProvider == EmailProvider.Smtp && WillSend;

    /// <summary>Resolves the admin order-notification recipient: <c>Email__AdminOrderNotify</c> wins,
    /// then <paramref name="adminOrderNotifyEmailFallback"/> (typically <c>Admin__OrderNotifyEmail</c>),
    /// then the hardcoded default.</summary>
    public string ResolveAdminOrderNotifyEmail(string? adminOrderNotifyEmailFallback)
    {
        if (!IsPlaceholder(AdminOrderNotify))
        {
            return AdminOrderNotify.Trim();
        }

        if (!IsPlaceholder(adminOrderNotifyEmailFallback))
        {
            return adminOrderNotifyEmailFallback!.Trim();
        }

        return "alok73772@gmail.com";
    }
}

