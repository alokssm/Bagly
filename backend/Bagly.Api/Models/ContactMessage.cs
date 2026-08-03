namespace Bagly.Api.Models;

/// <summary>A public contact-form submission. Always emailed to the admin mailbox; row storage
/// here is best-effort (see <c>ContactController</c>) so a DB hiccup never blocks the email.</summary>
public class ContactMessage
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public bool EmailSent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
