namespace Bagly.Api.Models;

/// <summary>
/// Maps to Serilog's MSSqlServer sink table (dbo.Logs).
/// </summary>
public class SystemLog
{
    public int Id { get; set; }
    public string? Message { get; set; }
    public string? MessageTemplate { get; set; }
    public string? Level { get; set; }
    public DateTime TimeStamp { get; set; }
    public string? Exception { get; set; }
    public string? LogEvent { get; set; }
    public string? RequestPath { get; set; }
    public string? ActorEmail { get; set; }
    public string? AuditCategory { get; set; }
    public string? AuditAction { get; set; }
}
