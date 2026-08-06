using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Extensions;
using Bagly.Api.Models;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

/// <summary>Seller marketplace auth. Distinct from customer and admin auth.</summary>
[ApiController]
[Route("api/auth/seller")]
public class SellerAuthController(
    BaglyDbContext db,
    IAuditLogService auditLog) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<SellerRegisterResponse>> Register(
        [FromBody] SellerRegisterRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var businessName = request.BusinessName?.Trim() ?? string.Empty;
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        var password = request.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(businessName) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            return BadRequest(new { message = "Name, business name, email, and password are required." });
        }

        if (password.Length < 8)
        {
            return BadRequest(new { message = "Password must be at least 8 characters." });
        }

        if (password != request.ConfirmPassword)
        {
            return BadRequest(new { message = "Passwords do not match." });
        }

        var existing = await db.SellerUsers.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (existing is not null)
        {
            return Conflict(new { message = "A seller account with this email already exists." });
        }

        var seller = new SellerUser
        {
            Email = email,
            Name = name,
            BusinessName = businessName,
            Phone = phone,
            PasswordHash = PasswordHasher.Hash(password),
            Status = "Pending",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        db.SellerUsers.Add(seller);
        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "SellerAuth",
            action: "Register",
            message: $"Seller '{seller.Email}' registered (business: '{seller.BusinessName}', status: Pending).",
            actorEmail: seller.Email,
            entityType: "SellerUser",
            entityId: seller.Id.ToString(),
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        return Ok(new SellerRegisterResponse(
            seller.Id,
            seller.Email,
            seller.Name,
            seller.BusinessName,
            seller.Status,
            "Your seller account has been created and is pending approval. You'll be able to sign in once approved."));
    }
}
