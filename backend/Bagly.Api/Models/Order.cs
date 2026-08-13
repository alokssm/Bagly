namespace Bagly.Api.Models;

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CustomerUserId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Zip { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    /// <summary>Optional customer phone (required for Shiprocket shipment creation).</summary>
    public string? Phone { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Shipping { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "Confirmed";
    public string PaymentStatus { get; set; } = "NotRequired";
    public string? PaymentProvider { get; set; }
    public string? Currency { get; set; }
    public decimal? AmountInr { get; set; }
    public string? RazorpayOrderId { get; set; }
    public string? RazorpayPaymentId { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    /// <summary>Shiprocket platform order id from create/adhoc (not Bagly OrderNumber).</summary>
    public string? ShiprocketOrderId { get; set; }
    public string? ShiprocketShipmentId { get; set; }
    public string? ShiprocketStatus { get; set; }
    /// <summary>Last Shiprocket skip/API error (admin-visible; never contains credentials).</summary>
    public string? ShiprocketLastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<OrderItem> Items { get; set; } = [];
    /// <summary>Per-pickup Shiprocket adhoc creates (one row per pickup nickname group).</summary>
    public List<OrderShiprocketShipment> ShiprocketShipments { get; set; } = [];
}

public class OrderItem
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}
