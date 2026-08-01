using System.Globalization;
using System.Text;
using Bagly.Api.Models;

namespace Bagly.Api.Services;

public interface IOrderConfirmationEmailService
{
    Task SendAsync(Order order, CancellationToken cancellationToken = default);
}

public class OrderConfirmationEmailService(
    IEmailSender emailSender,
    ILogger<OrderConfirmationEmailService> logger) : IOrderConfirmationEmailService
{
    public async Task SendAsync(Order order, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(order.Email))
        {
            logger.LogWarning(
                "Order confirmation email skipped for {OrderNumber}: customer email is missing on the order record.",
                order.OrderNumber);
            return;
        }

        var (subject, textBody, htmlBody) = BuildMessage(order);
        await emailSender.SendAsync(order.Email, subject, textBody, htmlBody, cancellationToken);
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

        return FormatMoney(order.Total, order.Currency ?? "INR");
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
        var code = string.IsNullOrWhiteSpace(currency) ? "INR" : currency.Trim().ToUpperInvariant();
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
