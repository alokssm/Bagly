namespace Bagly.Api.Models;

/// <summary>
/// Append-only history of courier tracking status changes for a Shiprocket shipment.
/// Current status is also mirrored on <see cref="OrderShiprocketShipment.TrackingStatus"/>.
/// </summary>
public class OrderShipmentTracking
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrderId { get; set; }
    public Order? Order { get; set; }

    public Guid OrderShiprocketShipmentId { get; set; }
    public OrderShiprocketShipment? OrderShiprocketShipment { get; set; }

    public string? ShiprocketShipmentId { get; set; }
    public string? AwbCode { get; set; }

    /// <summary>
    /// <see cref="ShipmentTrackingStatus"/> value, e.g. PICKUP_REQUESTED, IN_TRANSIT, DELIVERED.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Admin | ShiprocketWebhook | System</summary>
    public string Source { get; set; } = ShipmentTrackingStatus.SourceSystem;

    /// <summary>Optional raw webhook / API payload snippet (truncated).</summary>
    public string? RawJson { get; set; }
}
