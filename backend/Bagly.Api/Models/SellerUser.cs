namespace Bagly.Api.Models;

/// <summary>
/// Marketplace seller account. Separate from customers and admins.
/// Status starts as Pending until admin approval (future).
/// </summary>
public class SellerUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>Pending | Approved | Rejected | Suspended</summary>
    public string Status { get; set; } = "Pending";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
}
