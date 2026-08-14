namespace Bagly.Api.Models;

/// <summary>
/// Seller-owned Shiprocket pickup address nickname. Max 2 per seller (enforced in API).
/// Rows are persisted only after a successful Shiprocket <c>addpickup</c> call.
/// </summary>
public class SellerPickupLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SellerUserId { get; set; }

    /// <summary>Shiprocket pickup nickname (exact string used on products / adhoc create).</summary>
    public string PickupLocation { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Address2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = "India";
    public string PinCode { get; set; } = string.Empty;
    public string? Lat { get; set; }
    public string? Long { get; set; }
    public string? Gstin { get; set; }

    public bool ShiprocketSuccess { get; set; } = true;

    /// <summary>Optional id returned by Shiprocket addpickup response.</summary>
    public string? ShiprocketPickupId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public SellerUser? SellerUser { get; set; }
}
