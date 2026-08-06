namespace Bagly.Api.Models;

/// <summary>
/// Marketplace seller account. Separate from customers and admins.
/// Status: Pending (registered / awaiting review) | Approved | Rejected | Suspended.
/// First profile submit keeps/sets Pending for admin approval; Approved sellers stay Approved on later edits.
/// </summary>
public class SellerUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Business display name shown on the marketplace.</summary>
    public string BusinessName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = string.Empty;

    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    /// <summary>GST Identification Number — optional but recommended for India sellers.</summary>
    public string? Gstin { get; set; }
    public string? Description { get; set; }
    /// <summary>UPI ID for payouts (optional).</summary>
    public string? UpiId { get; set; }

    /// <summary>Pending | Approved | Rejected | Suspended</summary>
    public string Status { get; set; } = "Pending";
    public string? RejectionReason { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? ProfileSubmittedAt { get; set; }
}
