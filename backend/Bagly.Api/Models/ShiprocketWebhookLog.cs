namespace Bagly.Api.Models;

/// <summary>
/// Append-only log of every inbound Shiprocket webhook HTTP request/response
/// (<c>POST /api/webhooks/shiprocket</c>), including auth/parse/no-match failures.
/// </summary>
public class ShiprocketWebhookLog
{
    public long Id { get; set; }

    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;

    public string HttpMethod { get; set; } = "POST";

    public string Path { get; set; } = "/api/webhooks/shiprocket";

    /// <summary>Request headers as JSON; secret header values are masked.</summary>
    public string? HeadersJson { get; set; }

    /// <summary>Raw request body (truncated).</summary>
    public string? RequestBody { get; set; }

    public int ResponseStatusCode { get; set; }

    /// <summary>JSON body returned to Shiprocket (truncated).</summary>
    public string? ResponseBody { get; set; }

    /// <summary>True when the webhook was authorized and handled without unexpected errors.</summary>
    public bool ProcessedOk { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Bagly <see cref="Order"/> Id when a shipment was matched.</summary>
    public Guid? MatchedOrderId { get; set; }

    /// <summary>Bagly <see cref="OrderShiprocketShipment"/> Id when matched.</summary>
    public Guid? MatchedShipmentId { get; set; }

    /// <summary>Last mapped/applied tracking status (e.g. IN_TRANSIT).</summary>
    public string? MappedStatus { get; set; }
}
