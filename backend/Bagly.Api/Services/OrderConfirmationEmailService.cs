using System.Globalization;
using System.Text;
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
                "Order confirmation email skipped for {OrderNumber} to {Email}: SMTP not configured. Set Email__Host and Email__FromAddress (Render env vars or Email section in appsettings). HostSet={HostSet}, FromSet={FromSet}.",
                order.OrderNumber,
                order.Email,
                _options.HasSmtpHost,
                _options.HasFromAddress);
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
        var secureSocketOptions = ResolveSecureSocketOptions();

        logger.LogInformation(
            "Sending order confirmation email for {OrderNumber} to {Email} via {Host}:{Port} (SSL={UseSsl}, Auth={HasAuth})",
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

            using var client = new SmtpClient();
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
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to send order confirmation email for {OrderNumber} to {Email} via {Host}:{Port}. Check SMTP credentials, port, and provider settings (Gmail: app password + smtp.gmail.com:587; SendGrid: apikey user + smtp.sendgrid.net:587).",
                order.OrderNumber,
                order.Email,
                _options.Host.Trim(),
                _options.Port);
        }
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
        foreach (var item in order.Items)
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
            order.Items.Select(i =>
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
}
