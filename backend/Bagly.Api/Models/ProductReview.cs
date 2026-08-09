namespace Bagly.Api.Models;

public class ProductReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProductId { get; set; } = string.Empty;
    public Product? Product { get; set; }
    public Guid CustomerUserId { get; set; }
    public CustomerUser? CustomerUser { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
