namespace Bagly.Api.Services.Chat;

public sealed record ProductAvailabilityResult(
    bool Found,
    string? ProductId,
    string? ProductName,
    int StockQuantity,
    bool Available,
    string Message);

public sealed record StockAlertResult(
    bool Success,
    string? ProductId,
    string? ProductName,
    string Message);

public sealed record OrderStatusResult(
    bool Found,
    string? OrderNumber,
    string? Status,
    decimal? Total,
    string? Currency,
    IReadOnlyList<string> Items,
    string Message);
