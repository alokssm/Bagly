using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Bagly.Api.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Bagly.Api.Services;

/// <summary>
/// Shared transport for outbound email (SMTP/SendGrid/Resend), reused by order confirmations
/// and restock alerts so provider wiring only lives in one place.
/// </summary>
public interface IEmailSender
{
    /// <summary>Sends an email and returns whether it was (or should be considered) delivered.
    /// Returns false without throwing when email is disabled/unconfigured or the recipient is missing.</summary>
    Task<bool> SendAsync(
        string to,
        string subject,
        string textBody,
        string htmlBody,
        CancellationToken cancellationToken = default);
}

public class EmailSender(
    IOptions<EmailOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<EmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task<bool> SendAsync(
        string to,
        string subject,
        string textBody,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            logger.LogWarning("Email '{Subject}' skipped: recipient address is missing.", subject);
            return false;
        }

        if (!_options.IsConfigured)
        {
            logger.LogWarning(
                "Email '{Subject}' to {Email} skipped: email not configured. Provider={Provider}, HostSet={HostSet}, FromSet={FromSet}, SendGridKeySet={SendGridKeySet}, ResendKeySet={ResendKeySet}.",
                subject,
                to,
                _options.ResolvedProvider,
                _options.HasSmtpHost,
                _options.HasFromAddress,
                _options.HasSendGridApiKey,
                _options.HasResendApiKey);
            return false;
        }

        if (!_options.Enabled)
        {
            logger.LogWarning("Email '{Subject}' to {Email} skipped: Email__Enabled is false.", subject, to);
            return false;
        }

        return _options.ResolvedProvider switch
        {
            EmailProvider.SendGrid => await SendViaSendGridAsync(to, subject, textBody, htmlBody, cancellationToken),
            EmailProvider.Resend => await SendViaResendAsync(to, subject, textBody, htmlBody, cancellationToken),
            _ => await SendViaSmtpAsync(to, subject, textBody, htmlBody, cancellationToken),
        };
    }

    private async Task<bool> SendViaSendGridAsync(
        string to,
        string subject,
        string textBody,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        var apiKey = _options.ResolveSendGridApiKey()
            ?? throw new InvalidOperationException("SendGrid API key is missing.");

        logger.LogInformation(
            "Sending email '{Subject}' to {Email} via SendGrid HTTPS API (from {FromAddress})",
            subject,
            to,
            _options.FromAddress.Trim());

        var payload = new
        {
            personalizations = new[]
            {
                new
                {
                    to = new[] { new { email = to.Trim() } },
                },
            },
            from = new
            {
                email = _options.FromAddress.Trim(),
                name = _options.FromName,
            },
            subject,
            content = new object[]
            {
                new { type = "text/plain", value = textBody },
                new { type = "text/html", value = htmlBody },
            },
        };

        try
        {
            var client = httpClientFactory.CreateClient("SendGrid");
            using var request = new HttpRequestMessage(HttpMethod.Post, "v3/mail/send");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Email '{Subject}' sent to {Email} via SendGrid", subject, to);
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError(
                "Failed to send email '{Subject}' to {Email} via SendGrid: HTTP {StatusCode}. {ResponseBody}. Verify Email__FromAddress is a verified sender in SendGrid.",
                subject,
                to,
                (int)response.StatusCode,
                Truncate(responseBody, 500));
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email '{Subject}' to {Email} via SendGrid HTTPS API.", subject, to);
            return false;
        }
    }

    private async Task<bool> SendViaResendAsync(
        string to,
        string subject,
        string textBody,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        var apiKey = _options.ResolveResendApiKey()
            ?? throw new InvalidOperationException("Resend API key is missing.");

        var fromAddress = _options.FromAddress.Trim();
        var from = string.IsNullOrWhiteSpace(_options.FromName)
            ? fromAddress
            : $"{_options.FromName} <{fromAddress}>";

        logger.LogInformation(
            "Sending email '{Subject}' to {Email} via Resend HTTPS API (from {FromAddress})",
            subject,
            to,
            fromAddress);

        var payload = new
        {
            from,
            to = new[] { to.Trim() },
            subject,
            text = textBody,
            html = htmlBody,
        };

        try
        {
            var client = httpClientFactory.CreateClient("Resend");
            using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Email '{Subject}' sent to {Email} via Resend", subject, to);
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var sandboxHint = responseBody.Contains("testing emails", StringComparison.OrdinalIgnoreCase)
                || responseBody.Contains("only send", StringComparison.OrdinalIgnoreCase)
                || ((int)response.StatusCode == 403 &&
                    responseBody.Contains("domain", StringComparison.OrdinalIgnoreCase))
                ? " Resend is likely in test mode: verify bagly.co.in in Resend Domains and set Email__FromAddress=noreply@bagly.co.in (until then Resend only delivers to the account signup email)."
                : string.Empty;
            logger.LogError(
                "Failed to send email '{Subject}' to {Email} via Resend: HTTP {StatusCode}. {ResponseBody}.{SandboxHint} Verify Email__FromAddress is verified in Resend (or use onboarding@resend.dev for testing).",
                subject,
                to,
                (int)response.StatusCode,
                Truncate(responseBody, 500),
                sandboxHint);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email '{Subject}' to {Email} via Resend HTTPS API.", subject, to);
            return false;
        }
    }

    private async Task<bool> SendViaSmtpAsync(
        string to,
        string subject,
        string textBody,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        var secureSocketOptions = ResolveSecureSocketOptions();

        logger.LogInformation(
            "Sending email '{Subject}' to {Email} via SMTP {Host}:{Port} (SSL={UseSsl}, Auth={HasAuth})",
            subject,
            to,
            _options.Host.Trim(),
            _options.Port,
            secureSocketOptions,
            !string.IsNullOrWhiteSpace(_options.Username));

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress.Trim()));
            message.To.Add(MailboxAddress.Parse(to.Trim()));
            message.Subject = subject;
            message.Body = new BodyBuilder { TextBody = textBody, HtmlBody = htmlBody }.ToMessageBody();

            using var client = new SmtpClient
            {
                // Fail fast so callers are not blocked by SMTP hangs.
                Timeout = 15_000,
            };
            await client.ConnectAsync(_options.Host.Trim(), _options.Port, secureSocketOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                await client.AuthenticateAsync(_options.Username.Trim(), _options.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogInformation("Email '{Subject}' sent to {Email}", subject, to);
            return true;
        }
        catch (Exception ex) when (IsLikelyRenderSmtpBlock(ex))
        {
            logger.LogError(
                ex,
                "Failed to send email '{Subject}' to {Email} via SMTP {Host}:{Port}. " +
                "Render free tier blocks outbound SMTP ports 25/465/587 — set Email__Provider=Resend and Email__ResendApiKey (or SendGrid), or upgrade Render to a paid instance.",
                subject,
                to,
                _options.Host.Trim(),
                _options.Port);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to send email '{Subject}' to {Email} via SMTP {Host}:{Port}. " +
                "Gmail: app password + smtp.gmail.com:587, From must match account or alias. SendGrid SMTP: user apikey + smtp.sendgrid.net:587.",
                subject,
                to,
                _options.Host.Trim(),
                _options.Port);
            return false;
        }
    }

    private static bool IsLikelyRenderSmtpBlock(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("connect", StringComparison.OrdinalIgnoreCase) && message.Contains("587", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return ex is TimeoutException or OperationCanceledException;
    }

    private SecureSocketOptions ResolveSecureSocketOptions()
    {
        // Port 465 expects implicit TLS; 587 typically uses STARTTLS.
        if (_options.Port == 465)
        {
            return SecureSocketOptions.SslOnConnect;
        }

        return _options.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";
}
