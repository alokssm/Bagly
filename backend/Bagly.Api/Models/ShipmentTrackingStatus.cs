namespace Bagly.Api.Models;

/// <summary>
/// Courier tracking statuses after label / pickup (stored as strings on
/// <see cref="OrderShiprocketShipment.TrackingStatus"/> and history rows).
/// </summary>
public static class ShipmentTrackingStatus
{
    public const string PickupRequested = "PICKUP_REQUESTED";
    public const string PickedUp = "PICKED_UP";
    public const string InTransit = "IN_TRANSIT";
    public const string OutForDelivery = "OUT_FOR_DELIVERY";
    public const string Delivered = "DELIVERED";

    public const string SourceAdmin = "Admin";
    public const string SourceShiprocketWebhook = "ShiprocketWebhook";
    public const string SourceSystem = "System";

    /// <summary>Maps common Shiprocket webhook / track status labels to our enum strings.</summary>
    public static string? MapFromShiprocket(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToUpperInvariant().Replace('-', '_').Replace(' ', '_');

        if (s is "PICKUP_REQUESTED" or "PICKUP_SCHEDULED" or "SCHEDULED" or "PICKUP_GENERATED")
            return PickupRequested;
        if (s is "PICKED_UP" or "PICKEDUP" or "PICKUP_COMPLETED" or "SHIPPED")
            return PickedUp;
        if (s is "IN_TRANSIT" or "INTRANSIT" or "IN_TRANSIT_TO_CUSTOMER" or "REACHED_AT_DESTINATION_HUB"
            or "REACHED_WAREHOUSE" or "MISROUTED")
            return InTransit;
        if (s is "OUT_FOR_DELIVERY" or "OUTFORDELIVERY" or "OFD")
            return OutForDelivery;
        if (s is "DELIVERED" or "RTO_DELIVERED")
            return Delivered;

        // Looser contains checks for verbose Shiprocket labels.
        if (s.Contains("OUT_FOR_DELIVERY", StringComparison.Ordinal)) return OutForDelivery;
        if (s.Contains("DELIVERED", StringComparison.Ordinal)) return Delivered;
        if (s.Contains("PICKED", StringComparison.Ordinal)) return PickedUp;
        if (s.Contains("IN_TRANSIT", StringComparison.Ordinal) || s.Contains("INTRANSIT", StringComparison.Ordinal))
            return InTransit;
        if (s.Contains("PICKUP", StringComparison.Ordinal)) return PickupRequested;

        return null;
    }
}
