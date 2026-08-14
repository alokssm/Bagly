namespace Bagly.Api.Models;

/// <summary>Durable log of outbound Shiprocket HTTP requests (admin shipping + adhoc create).</summary>
public class ShiprocketApiLog
{
    public long Id { get; set; }

    public Guid? OrderId { get; set; }

    /// <summary>Bagly <see cref="OrderShiprocketShipment"/> Id when known.</summary>
    public Guid? ShipmentId { get; set; }

    /// <summary>e.g. Serviceability, AssignAwb, GenerateLabel, GeneratePickup, GenerateManifest, CreateAdhoc, PickupList.</summary>
    public string Action { get; set; } = string.Empty;

    public string HttpMethod { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    /// <summary>Exact request body or GET query string. Never contains Bearer tokens or passwords.</summary>
    public string? RequestJson { get; set; }

    public int? ResponseStatus { get; set; }

    public string? ResponseJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string? AdminEmail { get; set; }
}
