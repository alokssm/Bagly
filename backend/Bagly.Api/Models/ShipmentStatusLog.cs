namespace Bagly.Api.Models;

/// <summary>
/// Dedicated append-only audit log of every courier tracking status transition
/// (PICKUP_REQUESTED → … → DELIVERED). Complements <see cref="OrderShipmentTracking"/>
/// history used by the UI and the current status on <see cref="OrderShiprocketShipment"/>.
/// </summary>
public class ShipmentStatusLog
{
    public long Id { get; set; }

    public Guid OrderId { get; set; }
    public Order? Order { get; set; }

    /// <summary>Bagly <see cref="OrderShiprocketShipment"/> Id (uuid PK).</summary>
    public Guid OrderShiprocketShipmentId { get; set; }
    public OrderShiprocketShipment? OrderShiprocketShipment { get; set; }

    public string? AwbCode { get; set; }
    public string? ShiprocketShipmentId { get; set; }

    /// <summary>Previous <see cref="ShipmentTrackingStatus"/> value (null on first set).</summary>
    public string? FromStatus { get; set; }

    /// <summary>New <see cref="ShipmentTrackingStatus"/> value.</summary>
    public string ToStatus { get; set; } = string.Empty;

    /// <summary>Admin | ShiprocketWebhook | System</summary>
    public string Source { get; set; } = ShipmentTrackingStatus.SourceSystem;

    public string? Message { get; set; }

    /// <summary>Optional raw webhook / API payload snippet (truncated).</summary>
    public string? RawJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
