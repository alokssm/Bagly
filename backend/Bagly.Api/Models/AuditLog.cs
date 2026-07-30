namespace Bagly.Api.Models;

public class AuditLog
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Level { get; set; } = "Information";
    public string Category { get; set; } = "General";
    public string Action { get; set; } = string.Empty;
    public string? ActorEmail { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
    public string? IpAddress { get; set; }
    public string? RequestPath { get; set; }
}
