using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Bagly.Api.Models;
using Bagly.Api.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Bagly.Api.Services;

public interface IOrderConfirmationEmailService
{
    Task SendAsync(Order order, CancellationToken cancellationToken = default);
}

public class OrderConfirmationEmailService(
    IOptions<EmailOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<OrderConfirmationEmailService> logger) : IOrderConfirmationEmailService
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(Order order, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(order.Email))
        {
            logger.LogWarning(
                "Order confirmation email skipped for {OrderNumber}: customer email is missing on the order record.",
                order.OrderNumber);
            return;
        }

        if (!_options.IsConfigured)
        {
            logger.LogWarning(
                "Order confirmation email skipped for {OrderNumber} to {Email}: email not configured. Provider={Provider}, HostSet={HostSet}, FromSet={FromSet}, SendGridKeySet={SendGridKeySet}.",
                order.OrderNumber,
                order.Email,
                _options.ResolvedProvider,
                _options.HasSmtpHost,
                _options.HasFromAddress,
                _options.HasSendGridApiKey);
            return;
        }

        if (!_options.Enabled)
        {
            logger.LogWarning(
                "Order confirmation email skipped for {OrderNumber} to {Email}: Email__Enabled is false.",
                order.OrderNumber,
                order.Email);
            return;
        }

        var (subject, textBody, htmlBody) = BuildMessage(order);

        if (_options.ResolvedProvider == EmailProvider.SendGrid)
        {
            await SendViaSendGridAsync(order, subject, textBody, htmlBody, cancellationToken);
            return;
        }

        await SendViaSmtpAsync(order, subject, textBody, htmlBody, cancellationToken);
    }

    private async Task SendViaSendGridAsync(
        Order order,
        string subject,
        string textBody,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        var apiKey = _options.ResolveSendGridApiKey()
            ?? throw new InvalidOperationException("SendGrid API key is missing.");

        logger.LogInformation(
            "Sending order confirmation email for {OrderNumber} to {Email} via SendGrid HTTPS API (from {FromAddress})",
            order.OrderNumber,
            order.Email,
            _options.FromAddress.Trim());

        var payload = new
        {
            personalizations = new[]
            {
                new
                {
                    to = new[] { new { email = order.Email.Trim() } },
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
                logger.LogInformation(
                    "Order confirmation email sent for {OrderNumber} to {Email} via SendGrid",
                    order.OrderNumber,
                    order.Email);
                return;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError(
                "Failed to send order confirmation email for {OrderNumber} to {Email} via SendGrid: HTTP {StatusCode}. {ResponseBody}. Verify Email__FromAddress is a verified sender in SendGrid.",
                order.OrderNumber,
                order.Email,
                (int)response.StatusCode,
                Truncate(responseBody, 500));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to send order confirmation email for {OrderNumber} to {Email} via SendGrid HTTPS API.",
                order.OrderNumber,
                order.Email);
        }
    }

    private async Task SendViaSmtpAsync(
        Order order,
        string subject,
        string textBody,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        var secureSocketOptions = ResolveSecureSocketOptions();

        logger.LogInformation(
            "Sending order confirmation email for {OrderNumber} to {Email} via SMTP {Host}:{Port} (SSL={UseSsl}, Auth={HasAuth})",
            order.OrderNumber,
            order.Email,
            _options.Host.Trim(),
            _options.Port,
            secureSocketOptions,
            !string.IsNullOrWhiteSpace(_options.Username));

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress.Trim()));
            message.To.Add(MailboxAddress.Parse(order.Email.Trim()));
            message.Subject = subject;
            message.Body = new BodyBuilder { TextBody = textBody, HtmlBody = htmlBody }.ToMessageBody();

            using var client = new SmtpClient
            {
                // Fail fast so checkout verify/create responses are not blocked by SMTP hangs.
                Timeout = 15_000,
            };
            await client.ConnectAsync(_options.Host.Trim(), _options.Port, secureSocketOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                await client.AuthenticateAsync(_options.Username.Trim(), _options.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogInformation(
                "Order confirmation email sent for {OrderNumber} to {Email}",
                order.OrderNumber,
                order.Email);
        }
        catch (Exception ex) when (IsLikelyRenderSmtpBlock(ex))
        {
            logger.LogError(
                ex,
                "Failed to send order confirmation email for {OrderNumber} to {Email} via SMTP {Host}:{Port}. " +
                "Render free tier blocks outbound SMTP ports 25/465/587 — set Email__Provider=SendGrid and Email__SendGridApiKey, or upgrade Render to a paid instance.",
                order.OrderNumber,
                order.Email,
                _options.Host.Trim(),
                _options.Port);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to send order confirmation email for {OrderNumber} to {Email} via SMTP {Host}:{Port}. " +
                "Gmail: app password + smtp.gmail.com:587, From must match account or alias. SendGrid SMTP: user apikey + smtp.sendgrid.net:587.",
                order.OrderNumber,
                order.Email,
                _options.Host.Trim(),
                _options.Port);
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

    private static (string Subject, string TextBody, string HtmlBody) BuildMessage(Order order)
    {
        var items = order.Items ?? [];
        var customerName = $"{order.FirstName} {order.LastName}".Trim();
        var subject = $"Bagly order confirmed — {order.OrderNumber}";
        var amountLabel = FormatOrderTotal(order);
        var paymentLine = FormatPaymentLine(order);

        var sb = new StringBuilder();
        sb.AppendLine($"Hi{(string.IsNullOrWhiteSpace(customerName) ? "" : $" {customerName}")},");
        sb.AppendLine();
        sb.AppendLine($"Thank you for your order. Your order number is {order.OrderNumber}.");
        sb.AppendLine();
        sb.AppendLine(paymentLine);
        sb.AppendLine($"Total: {amountLabel}");
        sb.AppendLine();
        sb.AppendLine("Items:");
        foreach (var item in items)
        {
            var lineTotal = item.UnitPrice * item.Quantity;
            sb.AppendLine($"  • {item.ProductName} ({item.Color}) × {item.Quantity} — {FormatMoney(lineTotal, order.Currency)}");
        }

        sb.AppendLine();
        sb.AppendLine("Ship to:");
        sb.AppendLine(customerName);
        sb.AppendLine(order.Address);
        sb.AppendLine($"{order.City}, {order.State} {order.Zip}");
        sb.AppendLine(order.Country);
        sb.AppendLine();
        sb.AppendLine("We'll email you when your order ships.");
        sb.AppendLine();
        sb.AppendLine("— Bagly");

        var textBody = sb.ToString();

        var itemRows = string.Join(
            "",
            items.Select(i =>
            {
                var lineTotal = i.UnitPrice * i.Quantity;
                return $"""
                    <tr>
                      <td style="padding:8px 0;border-bottom:1px solid #eee;">{Escape(i.ProductName)}<br><small style="color:#666;">{Escape(i.Color)}</small></td>
                      <td style="padding:8px 0;border-bottom:1px solid #eee;text-align:center;">{i.Quantity}</td>
                      <td style="padding:8px 0;border-bottom:1px solid #eee;text-align:right;">{Escape(FormatMoney(lineTotal, order.Currency))}</td>
                    </tr>
                    """;
            }));

        var htmlBody = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;line-height:1.5;color:#222;max-width:560px;">
              <h2 style="margin:0 0 12px;">Order confirmed</h2>
              <p>Hi{(string.IsNullOrWhiteSpace(customerName) ? "" : $" {Escape(customerName)}")},</p>
              <p>Thank you for shopping with Bagly. Your order <strong>{Escape(order.OrderNumber)}</strong> is confirmed.</p>
              <p>{Escape(paymentLine)}<br><strong>Total: {Escape(amountLabel)}</strong></p>
              <table style="width:100%;border-collapse:collapse;margin:16px 0;">
                <thead>
                  <tr>
                    <th style="text-align:left;padding:8px 0;border-bottom:2px solid #ddd;">Item</th>
                    <th style="text-align:center;padding:8px 0;border-bottom:2px solid #ddd;">Qty</th>
                    <th style="text-align:right;padding:8px 0;border-bottom:2px solid #ddd;">Price</th>
                  </tr>
                </thead>
                <tbody>
                  {itemRows}
                </tbody>
              </table>
              <p><strong>Ship to</strong><br>
              {Escape(customerName)}<br>
              {Escape(order.Address)}<br>
              {Escape($"{order.City}, {order.State} {order.Zip}")}<br>
              {Escape(order.Country)}</p>
              <p style="color:#666;">We'll email you when your order ships.</p>
              <p>— Bagly</p>
            </div>
            """;

        return (subject, textBody, htmlBody);
    }

    private static string FormatOrderTotal(Order order)
    {
        if (string.Equals(order.PaymentProvider, "Razorpay", StringComparison.OrdinalIgnoreCase) && order.AmountInr is > 0)
        {
            return FormatMoney(order.AmountInr.Value, "INR");
        }

        return FormatMoney(order.Total, order.Currency ?? "USD");
    }

    private static string FormatPaymentLine(Order order) =>
        order.PaymentStatus switch
        {
            "Paid" => $"Payment received via {order.PaymentProvider ?? "online payment"}.",
            "NotRequired" => "Payment: not required for this order.",
            _ => $"Payment status: {order.PaymentStatus}.",
        };

    private static string FormatMoney(decimal amount, string? currency)
    {
        var code = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        return code switch
        {
            "INR" => $"₹{amount.ToString("N2", CultureInfo.InvariantCulture)}",
            "USD" => $"${amount.ToString("N2", CultureInfo.InvariantCulture)}",
            _ => $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {code}",
        };
    }

    private static string Escape(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";
}

