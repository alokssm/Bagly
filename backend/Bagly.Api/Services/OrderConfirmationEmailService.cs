using System.Globalization;
using System.Text;
using Bagly.Api.Models;
using Bagly.Api.Options;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Services;

public interface IOrderConfirmationEmailService
{
    Task SendAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the "item sold out during checkout" email for an order that reached
    /// <c>Order.Status == "OutOfStock"</c> in <c>PaymentsController.Verify</c> (the last-unit race).
    /// The wording adapts to <c>Order.PaymentStatus</c>: "Refunded" tells the customer the refund is
    /// done; anything else (refund attempt failed or was skipped) tells them it's pending/to contact
    /// support, so we never claim a refund happened when it didn't.
    /// </summary>
    Task SendOutOfStockRefundAsync(Order order, CancellationToken cancellationToken = default);
}

public class OrderConfirmationEmailService(
    IEmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    IOptions<AdminOptions> adminOptions,
    ILogger<OrderConfirmationEmailService> logger) : IOrderConfirmationEmailService
{
    public async Task SendAsync(Order order, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(order.Email))
        {
            logger.LogWarning(
                "Order confirmation email skipped for {OrderNumber}: customer email is missing on the order record.",
                order.OrderNumber);
        }
        else
        {
            var (subject, textBody, htmlBody) = BuildMessage(order);
            var sent = await emailSender.SendAsync(order.Email, subject, textBody, htmlBody, cancellationToken);
            if (sent)
            {
                logger.LogInformation("Order confirmation email sent for {OrderNumber}.", order.OrderNumber);
            }
            else
            {
                logger.LogWarning("Order confirmation email failed to send for {OrderNumber}.", order.OrderNumber);
            }
        }

        // Every successfully placed order (Razorpay verify success, or non-India CreateOrder
        // success) also gets an admin copy. Best-effort only — an admin-email failure must never
        // surface to the customer or the order flow, which has already completed by this point.
        await SendAdminOrderCopyAsync(order, cancellationToken);
    }

    private async Task SendAdminOrderCopyAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var adminEmail = emailOptions.Value.ResolveAdminOrderNotifyEmail(adminOptions.Value.OrderNotifyEmail);
            if (string.IsNullOrWhiteSpace(adminEmail))
            {
                return;
            }

            var (subject, textBody, htmlBody) = BuildAdminNotificationMessage(order);
            var sent = await emailSender.SendAsync(adminEmail, subject, textBody, htmlBody, cancellationToken);
            if (sent)
            {
                logger.LogInformation(
                    "Admin order notification sent for {OrderNumber} to {AdminEmail}.",
                    order.OrderNumber,
                    adminEmail);
            }
            else
            {
                logger.LogWarning(
                    "Admin order notification failed to send for {OrderNumber} to {AdminEmail}.",
                    order.OrderNumber,
                    adminEmail);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Admin order notification threw for {OrderNumber}; order remains confirmed and unaffected.",
                order.OrderNumber);
        }
    }

    public async Task SendOutOfStockRefundAsync(Order order, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(order.Email))
        {
            logger.LogWarning(
                "Out-of-stock/refund email skipped for {OrderNumber}: customer email is missing on the order record.",
                order.OrderNumber);
            return;
        }

        var (subject, textBody, htmlBody) = BuildOutOfStockRefundMessage(order);
        var sent = await emailSender.SendAsync(order.Email, subject, textBody, htmlBody, cancellationToken);
        if (sent)
        {
            logger.LogInformation(
                "Out-of-stock/refund email sent for {OrderNumber} (PaymentStatus={PaymentStatus}).",
                order.OrderNumber,
                order.PaymentStatus);
        }
        else
        {
            logger.LogWarning(
                "Out-of-stock/refund email failed to send for {OrderNumber} (PaymentStatus={PaymentStatus}).",
                order.OrderNumber,
                order.PaymentStatus);
        }
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

    /// <summary>Admin copy sent alongside every successful order confirmation. Order does not
    /// currently capture a phone number at checkout, so that line is a fixed placeholder.</summary>
    private static (string Subject, string TextBody, string HtmlBody) BuildAdminNotificationMessage(Order order)
    {
        var items = order.Items ?? [];
        var customerName = $"{order.FirstName} {order.LastName}".Trim();
        var amountLabel = FormatOrderTotal(order);
        var subject = $"New Bagly order — {order.OrderNumber} ({amountLabel})";

        var sb = new StringBuilder();
        sb.AppendLine("A new order was placed on Bagly.");
        sb.AppendLine();
        sb.AppendLine($"Order number: {order.OrderNumber}");
        sb.AppendLine($"Date: {order.CreatedAt:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"Status: {order.Status}");
        sb.AppendLine($"Payment status: {order.PaymentStatus}{(order.PaymentProvider is null ? "" : $" via {order.PaymentProvider}")}");
        sb.AppendLine();
        sb.AppendLine("Customer:");
        sb.AppendLine($"  Name: {(string.IsNullOrWhiteSpace(customerName) ? "—" : customerName)}");
        sb.AppendLine($"  Email: {order.Email}");
        sb.AppendLine("  Phone: not collected at checkout");
        sb.AppendLine();
        sb.AppendLine("Shipping address:");
        sb.AppendLine($"  {order.Address}");
        sb.AppendLine($"  {order.City}, {order.State} {order.Zip}");
        sb.AppendLine($"  {order.Country}");
        sb.AppendLine();
        sb.AppendLine("Items:");
        foreach (var item in items)
        {
            var lineTotal = item.UnitPrice * item.Quantity;
            sb.AppendLine($"  • {item.ProductName} ({item.Color}) × {item.Quantity} — {FormatMoney(lineTotal, order.Currency)}");
        }

        sb.AppendLine();
        sb.AppendLine($"Subtotal: {FormatMoney(order.Subtotal, order.Currency)}");
        sb.AppendLine($"Shipping: {FormatMoney(order.Shipping, order.Currency)}");
        sb.AppendLine($"Total: {amountLabel}");
        sb.AppendLine();
        sb.AppendLine("— Bagly admin notifications");

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
              <h2 style="margin:0 0 12px;">New order placed</h2>
              <p>
                <strong>Order:</strong> {Escape(order.OrderNumber)}<br>
                <strong>Date:</strong> {order.CreatedAt:yyyy-MM-dd HH:mm} UTC<br>
                <strong>Status:</strong> {Escape(order.Status)}<br>
                <strong>Payment:</strong> {Escape(order.PaymentStatus)}{(order.PaymentProvider is null ? "" : $" via {Escape(order.PaymentProvider)}")}
              </p>
              <p><strong>Customer</strong><br>
              {Escape(string.IsNullOrWhiteSpace(customerName) ? "—" : customerName)}<br>
              {Escape(order.Email)}<br>
              Phone: not collected at checkout</p>
              <p><strong>Ship to</strong><br>
              {Escape(order.Address)}<br>
              {Escape($"{order.City}, {order.State} {order.Zip}")}<br>
              {Escape(order.Country)}</p>
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
              <p><strong>Total: {Escape(amountLabel)}</strong></p>
              <p style="color:#666;">— Bagly admin notifications</p>
            </div>
            """;

        return (subject, textBody, htmlBody);
    }

    private static (string Subject, string TextBody, string HtmlBody) BuildOutOfStockRefundMessage(Order order)
    {
        var items = order.Items ?? [];
        var customerName = $"{order.FirstName} {order.LastName}".Trim();
        var amountLabel = FormatOrderTotal(order);
        var refunded = string.Equals(order.PaymentStatus, "Refunded", StringComparison.OrdinalIgnoreCase);

        var subject = refunded
            ? $"Bagly order {order.OrderNumber} — item sold out, payment refunded"
            : $"Bagly order {order.OrderNumber} — item sold out, refund pending";

        var refundLineText = refunded
            ? $"Your payment of {amountLabel} has been refunded to your original payment method. It typically takes 5–7 business days to appear, depending on your bank."
            : $"We attempted to refund your payment of {amountLabel} but it did not complete automatically. We're processing your refund — if you don't see it within 2 business days, please contact support with your order number so we can help right away.";

        var itemNames = items.Count > 0
            ? string.Join(", ", items.Select(i => i.ProductName))
            : "an item in your order";

        var sb = new StringBuilder();
        sb.AppendLine($"Hi{(string.IsNullOrWhiteSpace(customerName) ? "" : $" {customerName}")},");
        sb.AppendLine();
        sb.AppendLine($"We're sorry — {itemNames} sold out just as another customer completed checkout moments before you, so we were unable to fulfil order {order.OrderNumber}.");
        sb.AppendLine();
        sb.AppendLine(refundLineText);
        sb.AppendLine();
        sb.AppendLine("You're welcome to place a new order for any remaining items once you're ready.");
        sb.AppendLine();
        sb.AppendLine("We're sorry for the inconvenience.");
        sb.AppendLine();
        sb.AppendLine("— Bagly");

        var textBody = sb.ToString();

        var htmlBody = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;line-height:1.5;color:#222;max-width:560px;">
              <h2 style="margin:0 0 12px;">Item sold out during checkout</h2>
              <p>Hi{(string.IsNullOrWhiteSpace(customerName) ? "" : $" {Escape(customerName)}")},</p>
              <p>We're sorry — <strong>{Escape(itemNames)}</strong> sold out just as another customer completed checkout moments before you, so we were unable to fulfil order <strong>{Escape(order.OrderNumber)}</strong>.</p>
              <p>{Escape(refundLineText)}</p>
              <p>You're welcome to place a new order for any remaining items once you're ready.</p>
              <p style="color:#666;">We're sorry for the inconvenience.</p>
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
