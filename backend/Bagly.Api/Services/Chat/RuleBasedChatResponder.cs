using System.Text.RegularExpressions;
using Bagly.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Services.Chat;

/// <summary>
/// A small keyword/pattern engine that still uses the real tools (never invents stock or order data).
/// Active only when OpenAi__ApiKey is not configured.
/// </summary>
public sealed class RuleBasedChatResponder(BaglyDbContext db, IChatToolExecutor toolExecutor) : IRuleBasedChatResponder
{
    private static readonly Regex OrderNumberPattern = new(
        @"BG-[A-Za-z0-9-]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // TLD restricted to letters so trailing punctuation (e.g. a '?' ending a question) isn't captured.
    private static readonly Regex EmailPattern = new(
        @"[\w.+-]+@[\w-]+\.[A-Za-z]{2,}",
        RegexOptions.Compiled);

    public async Task<string> RespondAsync(string message, CancellationToken cancellationToken)
    {
        var text = message.Trim();
        var lower = text.ToLowerInvariant();

        var orderMatch = OrderNumberPattern.Match(text);
        var emailMatch = EmailPattern.Match(text);

        if (orderMatch.Success && emailMatch.Success)
        {
            var result = await toolExecutor.GetOrderStatusAsync(orderMatch.Value, emailMatch.Value, cancellationToken);
            return result.Message;
        }

        if (orderMatch.Success)
        {
            return "To look up that order, I'll also need the email address used when it was placed.";
        }

        if (ContainsAny(lower, "order status", "my order", "track my order", "where is my order") && !emailMatch.Success)
        {
            return "Sure — could you share the order number (like BG-20260731-1234) and the email used on the order?";
        }

        var wantsAlert = ContainsAny(lower, "alert", "notify", "let me know", "email me", "restock", "back in stock");
        var productName = await FindProductNameInTextAsync(lower, cancellationToken);

        if (wantsAlert)
        {
            if (productName is null)
            {
                return "Which product would you like a restock alert for?";
            }

            if (!emailMatch.Success)
            {
                return $"Sure — what email address should I use to notify you when {productName} is back in stock?";
            }

            var result = await toolExecutor.CreateStockAlertAsync(productName, emailMatch.Value, cancellationToken);
            return result.Message;
        }

        var wantsStock = ContainsAny(lower, "stock", "available", "availability", "have any", "do you have", "sold out", "carry", "in-stock");
        if (wantsStock || productName is not null)
        {
            if (productName is null)
            {
                return "Which product would you like me to check stock for?";
            }

            var result = await toolExecutor.CheckAvailabilityAsync(productName, cancellationToken);
            return result.Message;
        }

        if (emailMatch.Success)
        {
            return "Got your email — would you like me to check stock, set up a restock alert, or look up an order (I'll also need the order number)?";
        }

        return "I can check product stock, set up a restock alert for an out-of-stock item, or look up an order " +
               "status (with your order number and email). What would you like to do?";
    }

    private async Task<string?> FindProductNameInTextAsync(string lowerText, CancellationToken cancellationToken)
    {
        var products = await db.Products.AsNoTracking()
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken);

        return products
            .Where(p => lowerText.Contains(p.Name.ToLowerInvariant()) || lowerText.Contains(p.Id.ToLowerInvariant()))
            .OrderByDescending(p => p.Name.Length)
            .Select(p => p.Name)
            .FirstOrDefault();
    }

    private static bool ContainsAny(string haystack, params string[] needles) =>
        needles.Any(needle => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
