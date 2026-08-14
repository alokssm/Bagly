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
    /// <summary>Shiprocket create status (e.g. NEW) or last API status string.</summary>
    public string? Status { get; set; }
    /// <summary>
    /// Admin shipping workflow: ReadyToShip → AwbAssigned → LabelGenerated → PickupRequested → ManifestGenerated
    /// (null until Ready to Ship).
    /// </summary>
    public string? ShippingStatus { get; set; }
    /// <summary>Last skip/API error for this group (never credentials).</summary>
    public string? LastError { get; set; }

    public string? AwbCode { get; set; }
    public int? CourierId { get; set; }
    public string? CourierName { get; set; }
    /// <summary>Courier rate selected from serviceability / assign response.</summary>
    public decimal? ActualShippingCharge { get; set; }
    public DateTime? ReadyToShipAt { get; set; }
    public DateTime? AwbAssignedAt { get; set; }
    /// <summary>Shiprocket label PDF / download URL from generate/label.</summary>
    public string? LabelUrl { get; set; }
    public DateTime? LabelGeneratedAt { get; set; }
    /// <summary>
    /// When the marketplace seller marked this pickup shipment ready.
    /// Admin Ready to Ship (courier serviceability) stays disabled until set.
    /// </summary>
    public DateTime? SellerReadyToShipAt { get; set; }

    /// <summary>When admin successfully requested courier pickup via Shiprocket generate/pickup.</summary>
    public DateTime? PickupRequestedAt { get; set; }
    /// <summary>Shiprocket pickup_token_number (or similar) when returned by generate/pickup.</summary>
    public string? PickupTokenNumber { get; set; }

    /// <summary>Shiprocket manifest PDF / download URL from manifests/generate.</summary>
    public string? ManifestUrl { get; set; }
    public DateTime? ManifestGeneratedAt { get; set; }

    /// <summary>
    /// Current courier tracking status for quick queries
    /// (<see cref="ShipmentTrackingStatus"/>: PICKUP_REQUESTED → … → DELIVERED).
    /// </summary>
    public string? TrackingStatus { get; set; }
    public DateTime? TrackingStatusUpdatedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
