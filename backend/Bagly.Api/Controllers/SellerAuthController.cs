using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Extensions;
using Bagly.Api.Models;
using Bagly.Api.Options;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Controllers;

/// <summary>Seller marketplace auth. Distinct from customer and admin auth.</summary>
[ApiController]
[Route("api/auth/seller")]
public class SellerAuthController(
    BaglyDbContext db,
    IOptions<JwtOptions> jwtOptions,
    TokenService tokenService,
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
            "Your seller account has been created. You can sign in now — product listing will be available after approval."));
    }

    [HttpPost("login")]
    public async Task<ActionResult<SellerAuthResponse>> Login(
        [FromBody] SellerLoginRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Email and password are required." });
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var seller = await db.SellerUsers
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive, cancellationToken);

        if (seller is null || !PasswordHasher.Verify(request.Password, seller.PasswordHash))
        {
            await auditLog.LogAsync(
                category: "SellerAuth",
                action: "LoginFailed",
                message: $"Failed seller login attempt for '{email}'.",
                level: "Warning",
                actorEmail: email,
                entityType: "SellerUser",
                ipAddress: HttpContext.GetClientIp(),
                requestPath: HttpContext.GetRequestPath(),
                cancellationToken: cancellationToken);

            return Unauthorized(new { message = "Invalid email or password." });
        }

        // Pending sellers may sign in; product CRUD can gate on Status later.
        seller.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "SellerAuth",
            action: "LoginSuccess",
            message: $"Seller '{seller.Email}' logged in (status: {seller.Status}).",
            actorEmail: seller.Email,
            entityType: "SellerUser",
            entityId: seller.Id.ToString(),
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        return Ok(BuildResponse(seller));
    }

    [Authorize(Roles = "Seller")]
    [HttpGet("me")]
    public async Task<ActionResult<SellerAuthResponse>> Me(CancellationToken cancellationToken)
    {
        var idClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(idClaim, out var sellerId))
        {
            return Unauthorized(new { message = "Invalid seller session." });
        }

        var seller = await db.SellerUsers
            .FirstOrDefaultAsync(u => u.Id == sellerId && u.IsActive, cancellationToken);

        if (seller is null)
        {
            return Unauthorized(new { message = "Seller account not found." });
        }

        return Ok(BuildResponse(seller));
    }

    private SellerAuthResponse BuildResponse(SellerUser seller)
    {
        var token = tokenService.CreateSellerToken(seller.Id, seller.Email, seller.Name);
        var expiresAt = DateTime.UtcNow.AddMinutes(jwtOptions.Value.ExpiresMinutes);
        return new SellerAuthResponse(
            token,
            seller.Id,
            seller.Email,
            seller.Name,
            seller.BusinessName,
            seller.Status,
            expiresAt);
    }
}
