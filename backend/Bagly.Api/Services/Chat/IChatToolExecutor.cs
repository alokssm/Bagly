namespace Bagly.Api.Services.Chat;

public interface IChatToolExecutor
{
    /// <summary>Runs a named tool with JSON-encoded arguments and returns a JSON-encoded result (for the agent transcript).</summary>
    Task<string> ExecuteAsync(string toolName, string argumentsJson, CancellationToken cancellationToken);

    Task<ProductAvailabilityResult> CheckAvailabilityAsync(string productQuery, CancellationToken cancellationToken);

    Task<StockAlertResult> CreateStockAlertAsync(string productQuery, string email, CancellationToken cancellationToken);

    Task<OrderStatusResult> GetOrderStatusAsync(string orderNumber, string email, CancellationToken cancellationToken);
}
