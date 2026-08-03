using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Models;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Bagly.Api.Controllers;

/// <summary>Public "Contact us" form. No auth required (this is the storefront contact page),
/// and lightly rate-limited per IP so it can't be used to spam the admin mailbox.</summary>
[ApiController]
[Route("api/[controller]")]
public class ContactController(
    BaglyDbContext db,
    IContactEmailService emailService,
    IContactRateLimiter rateLimiter,
    ILogger<ContactController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] ContactRequest request, CancellationToken cancellationToken)
    {
        var validation = ValidateRequest(request);
        if (validation is not null)
        {
            return BadRequest(new { message = validation });
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!rateLimiter.TryConsume(ip))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = "Too many requests. Please try again in a few minutes.",
            });
        }

        var message = new ContactMessage
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Phone = request.Phone.Trim(),
            Email = request.Email.Trim(),
            CompanyName = string.IsNullOrWhiteSpace(request.CompanyName) ? null : request.CompanyName.Trim(),
            Message = request.Message.Trim(),
            IpAddress = ip,
            CreatedAt = DateTime.UtcNow,
        };

        // Storing the message is a nice-to-have; the admin email is the required delivery path,
        // so a DB hiccup here must never block the submission from reaching the customer.
        try
        {
            db.ContactMessages.Add(message);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist contact message from {Email}.", message.Email);
        }

        var sent = await emailService.SendAdminNotificationAsync(message, cancellationToken);

        if (message.Id != 0 && sent)
        {
            message.EmailSent = true;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // Best-effort flag update only — not worth failing the request over.
            }
        }

        return Ok(new { message = "Thanks for reaching out — we'll get back to you soon." });
    }

    private static string? ValidateRequest(ContactRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.Phone) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Message))
        {
            return "Please fill in first name, last name, phone, email, and message.";
        }

        if (request.FirstName.Trim().Length > 100 || request.LastName.Trim().Length > 100)
        {
            return "First/last name is too long.";
        }

        var email = request.Email.Trim();
        if (email.Length > 256 || !email.Contains('@', StringComparison.Ordinal))
        {
            return "Enter a valid email address.";
        }

        if (request.Phone.Trim().Length > 30)
        {
            return "Phone number is too long.";
        }

        if (request.Message.Trim().Length > 4000)
        {
            return "Message is too long (max 4000 characters).";
        }

        if (!string.IsNullOrWhiteSpace(request.CompanyName) && request.CompanyName.Trim().Length > 200)
        {
            return "Company name is too long.";
        }

        return null;
    }
}
