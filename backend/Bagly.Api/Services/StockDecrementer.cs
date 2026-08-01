using Bagly.Api.Data;
using Bagly.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Services;

/// <summary>Outcome of attempting to atomically decrement stock for a set of order lines.</summary>
public sealed record StockDecrementResult(bool Success, string? InsufficientProductId, string? InsufficientProductName);

/// <summary>
/// Atomically decrements <see cref="Product.StockQuantity"/> for each order line using a
/// DB-level conditional UPDATE (WHERE StockQuantity >= qty). SQL Server holds a row lock on the
/// updated row until the surrounding transaction commits or rolls back, so if two checkouts race
/// for the last unit, the second UPDATE blocks until the first transaction finishes and then
/// re-evaluates the predicate against the committed value — only one of the two can succeed.
/// Must be called inside a transaction shared with the order status update so a failed line
/// rolls back any decrements already applied for earlier lines of the same order.
/// </summary>
public static class StockDecrementer
{
    public static async Task<StockDecrementResult> TryDecrementAsync(
        BaglyDbContext db,
        IEnumerable<OrderItem> items,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            var affected = await db.Products
                .Where(p => p.Id == item.ProductId && p.StockQuantity >= item.Quantity)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(p => p.StockQuantity, p => p.StockQuantity - item.Quantity),
                    cancellationToken);

            if (affected == 0)
            {
                return new StockDecrementResult(false, item.ProductId, item.ProductName);
            }
        }

        return new StockDecrementResult(true, null, null);
    }
}
