namespace Bagly.Api.Models;

public class PaymentLog
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public Guid? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public string Provider { get; set; } = "Razorpay";
    public string EventType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? RazorpayOrderId { get; set; }
    public string? RazorpayPaymentId { get; set; }
    public string? RazorpaySignature { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? CustomerEmail { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? RequestJson { get; set; }
    public string? ResponseJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? IpAddress { get; set; }
}
