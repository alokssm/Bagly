using System.Text.RegularExpressions;
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

/// <summary>Seller marketplace auth and business profile. Distinct from customer and admin auth.</summary>
[ApiController]
[Route("api/auth/seller")]
public class SellerAuthController(
    BaglyDbContext db,
    IOptions<JwtOptions> jwtOptions,
    TokenService tokenService,
    IAuditLogService auditLog) : ControllerBase
{
    private static readonly Regex PincodeRegex = new(@"^\d{6}$", RegexOptions.Compiled);
    private static readonly Regex GstinRegex = new(
        @"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [HttpPost("register")]
    public async Task<ActionResult<SellerAuthResponse>> Register(
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
            LastLoginAt = DateTime.UtcNow,
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

        // Same pattern as customer register: return a session so the client can enter the seller hub.
        return Ok(BuildAuthResponse(seller));
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

        return Ok(BuildAuthResponse(seller));
    }

    [Authorize(Roles = "Seller")]
    [HttpGet("me")]
    public async Task<ActionResult<SellerAuthResponse>> Me(CancellationToken cancellationToken)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null)
        {
            return Unauthorized(new { message = "Seller account not found." });
        }

        return Ok(BuildAuthResponse(seller));
    }

    [Authorize(Roles = "Seller")]
    [HttpGet("profile")]
    public async Task<ActionResult<SellerProfileDto>> GetProfile(CancellationToken cancellationToken)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null)
        {
            return Unauthorized(new { message = "Seller account not found." });
        }

        return Ok(ToProfileDto(seller));
    }

    [Authorize(Roles = "Seller")]
    [HttpPut("profile")]
    public async Task<ActionResult<SellerProfileDto>> UpdateProfile(
        [FromBody] UpdateSellerProfileRequest request,
        CancellationToken cancellationToken)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null)
        {
            return Unauthorized(new { message = "Seller account not found." });
        }

        if (string.Equals(seller.Status, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Approved seller profiles cannot be changed. Contact Bagly support if you need an update.",
            });
        }

        var name = request.Name?.Trim() ?? string.Empty;
        var businessName = request.BusinessName?.Trim() ?? string.Empty;
        var phone = request.Phone?.Trim() ?? string.Empty;
        var addressLine1 = request.AddressLine1?.Trim() ?? string.Empty;
        var addressLine2 = string.IsNullOrWhiteSpace(request.AddressLine2) ? null : request.AddressLine2.Trim();
        var city = request.City?.Trim() ?? string.Empty;
        var state = request.State?.Trim() ?? string.Empty;
        var pincode = request.Pincode?.Trim() ?? string.Empty;
        var gstin = string.IsNullOrWhiteSpace(request.Gstin) ? null : request.Gstin.Trim().ToUpperInvariant();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var upiId = string.IsNullOrWhiteSpace(request.UpiId) ? null : request.UpiId.Trim();

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(businessName) ||
            string.IsNullOrWhiteSpace(phone) ||
            string.IsNullOrWhiteSpace(addressLine1) ||
            string.IsNullOrWhiteSpace(city) ||
            string.IsNullOrWhiteSpace(state) ||
            string.IsNullOrWhiteSpace(pincode))
        {
            return BadRequest(new
            {
                message = "Name, business name, phone, address line 1, city, state, and pincode are required.",
            });
        }

        if (phone.Length is < 8 or > 20)
        {
            return BadRequest(new { message = "Enter a valid contact phone number." });
        }

        if (!PincodeRegex.IsMatch(pincode))
        {
            return BadRequest(new { message = "Pincode must be a 6-digit Indian postal code." });
        }

        if (gstin is not null && !GstinRegex.IsMatch(gstin))
        {
            return BadRequest(new { message = "GSTIN format looks invalid. Leave it blank or enter a valid 15-character GSTIN." });
        }

        if (description is { Length: > 500 })
        {
            return BadRequest(new { message = "Business description must be 500 characters or fewer." });
        }

        var previousStatus = seller.Status;

        seller.Name = name;
        seller.BusinessName = businessName;
        seller.Phone = phone;
        seller.AddressLine1 = addressLine1;
        seller.AddressLine2 = addressLine2;
        seller.City = city;
        seller.State = state;
        seller.Pincode = pincode;
        seller.Gstin = gstin;
        seller.Description = description;
        seller.UpiId = upiId;
        seller.ProfileSubmittedAt = DateTime.UtcNow;

        // Pending / Rejected re-submit → Pending for admin review.
        // Suspended accounts keep Suspended until an admin changes them.
        // Approved is blocked above (403).
        if (string.Equals(previousStatus, "Suspended", StringComparison.OrdinalIgnoreCase))
        {
            // Keep Suspended.
        }
        else
        {
            seller.Status = "Pending";
            seller.RejectionReason = null;
            seller.ApprovedAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "SellerAuth",
            action: "UpdateProfile",
            message: $"Seller '{seller.Email}' submitted business details (status: {seller.Status}).",
            actorEmail: seller.Email,
            entityType: "SellerUser",
            entityId: seller.Id.ToString(),
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        return Ok(ToProfileDto(seller));
    }

    private async Task<SellerUser?> LoadCurrentSellerAsync(CancellationToken cancellationToken)
    {
        var idClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(idClaim, out var sellerId))
        {
            return null;
        }

        return await db.SellerUsers
            .FirstOrDefaultAsync(u => u.Id == sellerId && u.IsActive, cancellationToken);
    }

    private SellerAuthResponse BuildAuthResponse(SellerUser seller)
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

    private static SellerProfileDto ToProfileDto(SellerUser s) =>
        new(
            s.Id,
            s.Email,
            s.Name,
            s.BusinessName,
            s.Phone,
            s.AddressLine1,
            s.AddressLine2,
            s.City,
            s.State,
            s.Pincode,
            s.Gstin,
            s.Description,
            s.UpiId,
            s.Status,
            s.RejectionReason,
            s.ApprovedAt,
            s.ProfileSubmittedAt,
            IsProfileComplete(s));

    private static bool IsProfileComplete(SellerUser s) =>
        s.ProfileSubmittedAt != null &&
        !string.IsNullOrWhiteSpace(s.Phone) &&
        !string.IsNullOrWhiteSpace(s.AddressLine1) &&
        !string.IsNullOrWhiteSpace(s.City) &&
        !string.IsNullOrWhiteSpace(s.State) &&
        !string.IsNullOrWhiteSpace(s.Pincode);
}
