using Bagly.Api.Data;
using Bagly.Api.Models;
using Bagly.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Services;

public interface IStockAlertNotifier
{
    /// <summary>Emails every pending (Notified == false) StockAlert for a product that is back in stock,
    /// marking each as Notified on success. Never throws — failures are logged so one bad alert/email
    /// doesn't block the others or the caller (e.g. an admin product update).</summary>
    Task NotifyRestockAsync(string productId, CancellationToken cancellationToken = default);
}

public class StockAlertNotifier(
    BaglyDbContext db,
    IEmailSender emailSender,
    IOptions<StorefrontOptions> storefrontOptions,
    IConfiguration configuration,
    ILogger<StockAlertNotifier> logger) : IStockAlertNotifier
{
    public async Task NotifyRestockAsync(string productId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productId))
        {
            return;
        }

        try
        {
            var product = await db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

            if (product is null || !product.IsAvailable)
            {
                return;
            }

            var pendingAlerts = await db.StockAlerts
                .Where(a => a.ProductId == productId && !a.Notified)
                .ToListAsync(cancellationToken);

            if (pendingAlerts.Count == 0)
            {
                return;
            }

            logger.LogInformation(
                "Restock notifier: sending {Count} pending stock alert(s) for product {ProductId} ({ProductName}).",
                pendingAlerts.Count,
                productId,
                product.Name);

            var (subject, textBody, htmlBody) = BuildMessage(product);
            var sentCount = 0;

            foreach (var alert in pendingAlerts)
            {
                try
                {
                    var sent = await emailSender.SendAsync(alert.Email, subject, textBody, htmlBody, cancellationToken);
                    if (sent)
                    {
                        alert.Notified = true;
                        alert.NotifiedAt = DateTime.UtcNow;
                        sentCount++;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Restock notifier: failed to email stock alert {AlertId} for product {ProductId} to {Email}.",
                        alert.Id,
                        productId,
                        alert.Email);
                }
            }

            if (sentCount > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            logger.LogInformation(
                "Restock notifier: sent {SentCount}/{TotalCount} stock alert email(s) for product {ProductId}.",
                sentCount,
                pendingAlerts.Count,
                productId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Restock notifier failed for product {ProductId}.", productId);
        }
    }

    private (string Subject, string TextBody, string HtmlBody) BuildMessage(Product product)
    {
        var subject = $"Back in stock at Bagly: {product.Name}";
        var link = BuildProductLink(product.Id);

        var textBody =
            $"""
            Good news — {product.Name} is back in stock at Bagly!

            {(link is null ? "" : $"View it here: {link}\n")}
            This alert has been cleared. If you'd like another alert next time it sells out, just ask us in chat.

            — Bagly
            """;

        var linkHtml = link is null
            ? string.Empty
            : $"""<p><a href="{Escape(link)}" style="display:inline-block;padding:10px 18px;background:#111;color:#fff;text-decoration:none;border-radius:6px;">View {Escape(product.Name)}</a></p>""";

        var htmlBody =
            $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;line-height:1.5;color:#222;max-width:560px;">
              <h2 style="margin:0 0 12px;">Back in stock!</h2>
              <p>Good news — <strong>{Escape(product.Name)}</strong> is back in stock at Bagly.</p>
              {linkHtml}
              <p style="color:#666;">This alert has been cleared. If you'd like another alert next time it sells out, just ask us in chat.</p>
              <p>— Bagly</p>
            </div>
            """;

        return (subject, textBody, htmlBody);
    }

    private string? BuildProductLink(string productId)
    {
        var baseUrl = ResolveStorefrontBaseUrl();
        return string.IsNullOrWhiteSpace(baseUrl)
            ? null
            : $"{baseUrl}/product/{Uri.EscapeDataString(productId)}";
    }

    private string? ResolveStorefrontBaseUrl()
    {
        var configured = storefrontOptions.Value.BaseUrl;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().TrimEnd('/');
        }

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        var firstOrigin = origins.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o));
        return firstOrigin?.Trim().TrimEnd('/');
    }

    private static string Escape(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
