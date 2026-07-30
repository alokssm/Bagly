using System.Text.Json;
using Bagly.Api.Data;
using Bagly.Api.Models;

namespace Bagly.Api.Services;

public interface IPaymentLogService
{
    Task LogAsync(
        string eventType,
        string status,
        string message,
        Guid? orderId = null,
        string? orderNumber = null,
        string? razorpayOrderId = null,
        string? razorpayPaymentId = null,
        string? razorpaySignature = null,
        decimal? amount = null,
        string? currency = null,
        string? customerEmail = null,
        object? request = null,
        object? response = null,
        string? errorCode = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);
}

public class PaymentLogService(BaglyDbContext db, ILogger<PaymentLogService> logger) : IPaymentLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public async Task LogAsync(
        string eventType,
        string status,
        string message,
        Guid? orderId = null,
        string? orderNumber = null,
        string? razorpayOrderId = null,
        string? razorpayPaymentId = null,
        string? razorpaySignature = null,
        decimal? amount = null,
        string? currency = null,
        string? customerEmail = null,
        object? request = null,
        object? response = null,
        string? errorCode = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new PaymentLog
        {
            TimestampUtc = DateTime.UtcNow,
            OrderId = orderId,
            OrderNumber = Truncate(orderNumber, 50),
            Provider = "Razorpay",
            EventType = Truncate(eventType, 50)!,
            Status = Truncate(status, 50)!,
            RazorpayOrderId = Truncate(razorpayOrderId, 100),
            RazorpayPaymentId = Truncate(razorpayPaymentId, 100),
            RazorpaySignature = Truncate(razorpaySignature, 256),
            Amount = amount,
            Currency = Truncate(currency, 10),
            CustomerEmail = Truncate(customerEmail, 256),
            Message = Truncate(message, 2000) ?? string.Empty,
            RequestJson = request is null ? null : JsonSerializer.Serialize(request, JsonOptions),
            ResponseJson = response is null ? null : JsonSerializer.Serialize(response, JsonOptions),
            ErrorCode = Truncate(errorCode, 100),
            IpAddress = Truncate(ipAddress, 64),
        };

        db.PaymentLogs.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "PaymentLog {EventType} {Status} Order={OrderNumber} RazorpayOrder={RazorpayOrderId}",
            entry.EventType,
            entry.Status,
            entry.OrderNumber,
            entry.RazorpayOrderId);
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..max];
}
