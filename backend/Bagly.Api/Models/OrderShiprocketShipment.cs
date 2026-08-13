namespace Bagly.Api.Models;

/// <summary>
/// One Shiprocket adhoc order for a Bagly order's pickup group.
/// Multi-pickup carts create multiple rows (e.g. home + work).
/// </summary>
public class OrderShiprocketShipment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }

    /// <summary>Exact Shiprocket pickup nickname used for this shipment (case-sensitive).</summary>
    public string PickupLocation { get; set; } = string.Empty;

    public string? ShiprocketOrderId { get; set; }
    public string? ShiprocketShipmentId { get; set; }
    public string? Status { get; set; }
    /// <summary>Last skip/API error for this group (never credentials).</summary>
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
