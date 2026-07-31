namespace Bagly.Api.Models;

/// <summary>A customer request to be emailed when a product is back in stock.</summary>
public class StockAlert
{
    public int Id { get; set; }
    public string ProductId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool Notified { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? NotifiedAt { get; set; }
}
