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

    /// <summary>Pipeline order; higher means later in the journey.</summary>
    public static int Rank(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return 0;
        return status.Trim().ToUpperInvariant() switch
        {
            PickupRequested => 1,
            PickedUp => 2,
            InTransit => 3,
            OutForDelivery => 4,
            Delivered => 5,
            _ => 0,
        };
    }

    /// <summary>True when <paramref name="next"/> is strictly ahead of <paramref name="current"/> (or current is empty).</summary>
    public static bool IsForwardOf(string? next, string? current)
    {
        var nextRank = Rank(next);
        if (nextRank <= 0) return false;
        var currentRank = Rank(current);
        return currentRank <= 0 || nextRank > currentRank;
    }

    /// <summary>Maps common Shiprocket webhook / track status labels to our enum strings.</summary>
    public static string? MapFromShiprocket(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToUpperInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');

        while (s.Contains("__", StringComparison.Ordinal))
        {
            s = s.Replace("__", "_", StringComparison.Ordinal);
        }

        if (s is "PICKUP_REQUESTED" or "PICKUP_SCHEDULED" or "SCHEDULED" or "PICKUP_GENERATED"
            or "PICKUP_QUEUED" or "AWB_ASSIGNED" or "LABEL_GENERATED")
        {
            return PickupRequested;
        }

        if (s is "PICKED_UP" or "PICKEDUP" or "PICKUP_COMPLETED" or "SHIPPED")
        {
            return PickedUp;
        }

        if (s is "IN_TRANSIT" or "INTRANSIT" or "IN_TRANSIT_TO_CUSTOMER" or "REACHED_AT_DESTINATION_HUB"
            or "REACHED_WAREHOUSE" or "MISROUTED" or "CONNECTED" or "IN_TRANSIT_OVERSEAS")
        {
            return InTransit;
        }

        if (s is "OUT_FOR_DELIVERY" or "OUTFORDELIVERY" or "OFD" or "OUT_FOR_DELIVERY_OD")
        {
            return OutForDelivery;
        }

        if (s is "DELIVERED" or "RTO_DELIVERED")
        {
            return Delivered;
        }

        // Verbose Shiprocket labels / scan activity text.
        if (s.Contains("OUT_FOR_DELIVERY", StringComparison.Ordinal) ||
            s.Contains("OUTFORDELIVERY", StringComparison.Ordinal))
        {
            return OutForDelivery;
        }

        if (s.Contains("DELIVERED", StringComparison.Ordinal)) return Delivered;

        if (s.Contains("IN_TRANSIT", StringComparison.Ordinal) ||
            s.Contains("INTRANSIT", StringComparison.Ordinal))
        {
            return InTransit;
        }

        if (s.Contains("PICKED_UP", StringComparison.Ordinal) ||
            s.Contains("PICKEDUP", StringComparison.Ordinal) ||
            s.Contains("SHIPMENT_PICKED", StringComparison.Ordinal) ||
            (s.Contains("PICKED", StringComparison.Ordinal) && s.Contains("UP", StringComparison.Ordinal)))
        {
            return PickedUp;
        }

        if (s.Contains("SHIPPED", StringComparison.Ordinal)) return PickedUp;

        if (s.Contains("PICKUP_REQUEST", StringComparison.Ordinal) ||
            s.Contains("PICKUP_SCHEDULE", StringComparison.Ordinal))
        {
            return PickupRequested;
        }

        // Ignore courier-only codes (X-*), MANIFEST GENERATED, NA, etc.
        return null;
    }

    /// <summary>Best-effort map of Shiprocket numeric status ids seen on webhooks.</summary>
    public static string? MapFromShiprocketStatusId(int? statusId)
    {
        if (statusId is null or <= 0) return null;
        return statusId.Value switch
        {
            1 or 2 or 3 or 4 or 5 or 9 or 10 => PickupRequested,
            42 or 6 => PickedUp,
            18 or 20 or 19 => InTransit,
            17 or 21 => OutForDelivery,
            7 or 8 or 14 => Delivered,
            _ => null,
        };
    }
}
