using System.Text.Json;
using System.Text.RegularExpressions;
using Bagly.Api.Data;
using Bagly.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Services.Chat;

/// <summary>Executes the chat agent's tools against the database. Shared by the OpenAI agent and the rule-based fallback.</summary>
public sealed class ChatToolExecutor(BaglyDbContext db, ILogger<ChatToolExecutor> logger) : IChatToolExecutor
{
    private static readonly Regex EmailPattern = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions ArgsReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions ResultWriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<string> ExecuteAsync(string toolName, string argumentsJson, CancellationToken cancellationToken)
    {
        try
        {
            switch (toolName)
            {
                case ChatToolDefinitions.CheckProductAvailability:
                {
                    var args = ParseArgs<ProductArgs>(argumentsJson);
                    var result = await CheckAvailabilityAsync(args.ProductName ?? string.Empty, cancellationToken);
                    return JsonSerializer.Serialize(result, ResultWriteOptions);
                }

                case ChatToolDefinitions.CreateStockAlert:
                {
                    var args = ParseArgs<StockAlertArgs>(argumentsJson);
                    var result = await CreateStockAlertAsync(args.ProductName ?? string.Empty, args.Email ?? string.Empty, cancellationToken);
                    return JsonSerializer.Serialize(result, ResultWriteOptions);
                }

                case ChatToolDefinitions.GetOrderStatus:
                {
                    var args = ParseArgs<OrderStatusArgs>(argumentsJson);
                    var result = await GetOrderStatusAsync(args.OrderNumber ?? string.Empty, args.Email ?? string.Empty, cancellationToken);
                    return JsonSerializer.Serialize(result, ResultWriteOptions);
                }

                default:
                    logger.LogWarning("Chat agent requested unknown tool {ToolName}.", toolName);
                    return JsonSerializer.Serialize(new { error = $"Unknown tool '{toolName}'." }, ResultWriteOptions);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chat tool {ToolName} failed with arguments {Arguments}.", toolName, argumentsJson);
            return JsonSerializer.Serialize(new { error = "The tool failed to execute." }, ResultWriteOptions);
        }
    }

    public async Task<ProductAvailabilityResult> CheckAvailabilityAsync(string productQuery, CancellationToken cancellationToken)
    {
        var product = await FindProductAsync(productQuery, cancellationToken);
        if (product is null)
        {
            return new ProductAvailabilityResult(
                Found: false,
                ProductId: null,
                ProductName: null,
                StockQuantity: 0,
                Available: false,
                Message: $"I couldn't find a product matching '{productQuery}'. Could you check the name?");
        }

        var message = product.IsAvailable
            ? $"{product.Name} is in stock ({product.StockQuantity} available) at ₹{product.Price:0.00}."
            : product.IsActive
                ? $"{product.Name} is currently out of stock. I can set up an email alert for when it's back."
                : $"{product.Name} isn't currently available in the store.";

        return new ProductAvailabilityResult(
            Found: true,
            ProductId: product.Id,
            ProductName: product.Name,
            StockQuantity: product.StockQuantity,
            Available: product.IsAvailable,
            Message: message);
    }

    public async Task<StockAlertResult> CreateStockAlertAsync(string productQuery, string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim();
        if (!IsValidEmail(normalizedEmail))
        {
            return new StockAlertResult(
                Success: false,
                ProductId: null,
                ProductName: null,
                Message: "That doesn't look like a valid email address — could you double-check it?");
        }

        var product = await FindProductAsync(productQuery, cancellationToken);
        if (product is null)
        {
            return new StockAlertResult(
                Success: false,
                ProductId: null,
                ProductName: null,
                Message: $"I couldn't find a product matching '{productQuery}'. Could you check the name?");
        }

        if (product.IsAvailable)
        {
            return new StockAlertResult(
                Success: true,
                ProductId: product.Id,
                ProductName: product.Name,
                Message: $"Good news — {product.Name} is already in stock, so no alert is needed.");
        }

        var existing = await db.StockAlerts.FirstOrDefaultAsync(
            a => a.ProductId == product.Id && a.Email.ToLower() == normalizedEmail.ToLower(),
            cancellationToken);

        if (existing is null)
        {
            db.StockAlerts.Add(new StockAlert
            {
                ProductId = product.Id,
                Email = normalizedEmail,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (existing.Notified)
        {
            // Re-arm the alert for a new restock cycle.
            existing.Notified = false;
            existing.NotifiedAt = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        return new StockAlertResult(
            Success: true,
            ProductId: product.Id,
            ProductName: product.Name,
            Message: $"You're set — we'll email {normalizedEmail} as soon as {product.Name} is back in stock.");
    }

    public async Task<OrderStatusResult> GetOrderStatusAsync(string orderNumber, string email, CancellationToken cancellationToken)
    {
        var normalizedOrderNumber = orderNumber.Trim();
        var normalizedEmail = email.Trim();

        const string notFoundMessage =
            "I couldn't find an order with that order number and email combination. Please double-check both and try again.";

        if (string.IsNullOrWhiteSpace(normalizedOrderNumber) || string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return new OrderStatusResult(
                Found: false,
                OrderNumber: null,
                Status: null,
                Total: null,
                Currency: null,
                Items: [],
                Message: "I'll need both the order number (like BG-20260731-1234) and the email used on the order.");
        }

        // Matching on OrderNumber AND Email together means a mismatch on either one
        // produces the exact same "not found" response — order numbers can't be brute-forced.
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(
                o => o.OrderNumber.ToLower() == normalizedOrderNumber.ToLower() &&
                     o.Email.ToLower() == normalizedEmail.ToLower(),
                cancellationToken);

        if (order is null)
        {
            return new OrderStatusResult(
                Found: false,
                OrderNumber: null,
                Status: null,
                Total: null,
                Currency: null,
                Items: [],
                Message: notFoundMessage);
        }

        var itemSummaries = order.Items
            .Select(i => $"{i.ProductName} x{i.Quantity}")
            .ToList();

        var amount = string.Equals(order.PaymentProvider, "Razorpay", StringComparison.OrdinalIgnoreCase) && order.AmountInr is > 0
            ? order.AmountInr.Value
            : order.Total;
        var currency = string.Equals(order.PaymentProvider, "Razorpay", StringComparison.OrdinalIgnoreCase) && order.AmountInr is > 0
            ? "INR"
            : (order.Currency ?? "INR");

        return new OrderStatusResult(
            Found: true,
            OrderNumber: order.OrderNumber,
            Status: order.Status,
            Total: amount,
            Currency: currency,
            Items: itemSummaries,
            Message: $"Order {order.OrderNumber} is '{order.Status}'. Items: {string.Join(", ", itemSummaries)}. Total: {FormatMoney(amount, currency)}.");
    }

    private async Task<Product?> FindProductAsync(string query, CancellationToken cancellationToken)
    {
        var q = query.Trim();
        if (string.IsNullOrWhiteSpace(q))
        {
            return null;
        }

        var lowered = q.ToLowerInvariant();

        var byId = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id.ToLower() == lowered, cancellationToken);
        if (byId is not null)
        {
            return byId;
        }

        var exactName = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name.ToLower() == lowered, cancellationToken);
        if (exactName is not null)
        {
            return exactName;
        }

        return await db.Products.AsNoTracking()
            .Where(p => p.Name.ToLower().Contains(lowered) || p.Id.ToLower().Contains(lowered))
            .OrderBy(p => p.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool IsValidEmail(string value) =>
        !string.IsNullOrWhiteSpace(value) && EmailPattern.IsMatch(value);

    private static string FormatMoney(decimal amount, string currency) =>
        currency.ToUpperInvariant() switch
        {
            "INR" => $"₹{amount:0.00}",
            "USD" => $"${amount:0.00}",
            _ => $"{amount:0.00} {currency.ToUpperInvariant()}",
        };

    private static T ParseArgs<T>(string json) where T : new() =>
        string.IsNullOrWhiteSpace(json)
            ? new T()
            : JsonSerializer.Deserialize<T>(json, ArgsReadOptions) ?? new T();

    private sealed class ProductArgs
    {
        public string? ProductName { get; set; }
    }

    private sealed class StockAlertArgs
    {
        public string? ProductName { get; set; }
        public string? Email { get; set; }
    }

    private sealed class OrderStatusArgs
    {
        public string? OrderNumber { get; set; }
        public string? Email { get; set; }
    }
}
