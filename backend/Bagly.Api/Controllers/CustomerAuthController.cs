using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Extensions;
using Bagly.Api.Models;
using Bagly.Api.Options;
using Bagly.Api.Services;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Controllers;

/// <summary>Storefront customer auth: register, login, and Google sign-in. Distinct from admin auth (AuthController).</summary>
[ApiController]
[Route("api/auth/customer")]
public class CustomerAuthController(
    BaglyDbContext db,
    IOptions<JwtOptions> jwtOptions,
    IOptions<GoogleAuthOptions> googleOptions,
    TokenService tokenService,
    IAuditLogService auditLog,
    ILogger<CustomerAuthController> logger) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<CustomerAuthResponse>> Register(
        [FromBody] CustomerRegisterRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var password = request.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return BadRequest(new { message = "Name, email, and password are required." });
        }

        if (password.Length < 8)
        {
            return BadRequest(new { message = "Password must be at least 8 characters." });
        }

        if (password != request.ConfirmPassword)
        {
            return BadRequest(new { message = "Passwords do not match." });
        }

        var existing = await db.CustomerUsers.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (existing is not null)
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        var customer = new CustomerUser
        {
            Email = email,
            Name = name,
            PasswordHash = PasswordHasher.Hash(password),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow,
        };

        db.CustomerUsers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "CustomerAuth",
            action: "Register",
            message: $"Customer '{customer.Email}' registered.",
            actorEmail: customer.Email,
            entityType: "CustomerUser",
            entityId: customer.Id.ToString(),
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        return Ok(BuildResponse(customer));
    }

    [HttpPost("login")]
    public async Task<ActionResult<CustomerAuthResponse>> Login(
        [FromBody] CustomerLoginRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Email and password are required." });
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var customer = await db.CustomerUsers
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive, cancellationToken);

        if (customer is null || string.IsNullOrEmpty(customer.PasswordHash) ||
            !PasswordHasher.Verify(request.Password, customer.PasswordHash))
        {
            await auditLog.LogAsync(
                category: "CustomerAuth",
                action: "LoginFailed",
                message: $"Failed customer login attempt for '{email}'.",
                level: "Warning",
                actorEmail: email,
                entityType: "CustomerUser",
                ipAddress: HttpContext.GetClientIp(),
                requestPath: HttpContext.GetRequestPath(),
                cancellationToken: cancellationToken);

            var hint = customer is not null && string.IsNullOrEmpty(customer.PasswordHash)
                ? "This account uses Google sign-in. Please continue with Google."
                : "Invalid email or password.";
            return Unauthorized(new { message = hint });
        }

        customer.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "CustomerAuth",
            action: "LoginSuccess",
            message: $"Customer '{customer.Email}' logged in.",
            actorEmail: customer.Email,
            entityType: "CustomerUser",
            entityId: customer.Id.ToString(),
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        return Ok(BuildResponse(customer));
    }

    [HttpGet("google-config")]
    public ActionResult<GoogleAuthConfigDto> GetGoogleConfig()
    {
        var opts = googleOptions.Value;
        return Ok(new GoogleAuthConfigDto(
            opts.IsConfigured,
            opts.IsConfigured ? opts.ClientId : null));
    }

    [HttpPost("google")]
    public async Task<ActionResult<CustomerAuthResponse>> GoogleLogin(
        [FromBody] CustomerGoogleLoginRequest request,
        CancellationToken cancellationToken)
    {
        if (!googleOptions.Value.IsConfigured)
        {
            return StatusCode(503, new { message = "Google sign-in is not configured on the server yet." });
        }

        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            return BadRequest(new { message = "Missing Google ID token." });
        }

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(
                request.IdToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = [googleOptions.Value.ClientId],
                });
        }
        catch (InvalidJwtException ex)
        {
            logger.LogWarning(ex, "Google ID token validation failed.");
            return Unauthorized(new { message = "Invalid Google sign-in token." });
        }

        var email = payload.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { message = "Google account has no email address." });
        }

        var customer = await db.CustomerUsers
            .FirstOrDefaultAsync(u => u.GoogleSubject == payload.Subject, cancellationToken);

        customer ??= await db.CustomerUsers.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        var isNew = false;
        if (customer is null)
        {
            customer = new CustomerUser
            {
                Email = email,
                Name = string.IsNullOrWhiteSpace(payload.Name) ? email.Split('@')[0] : payload.Name,
                GoogleSubject = payload.Subject,
                PasswordHash = null,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };
            db.CustomerUsers.Add(customer);
            isNew = true;
        }
        else if (string.IsNullOrEmpty(customer.GoogleSubject))
        {
            customer.GoogleSubject = payload.Subject;
        }

        if (!customer.IsActive)
        {
            return Unauthorized(new { message = "This account is deactivated." });
        }

        customer.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "CustomerAuth",
            action: isNew ? "GoogleRegister" : "GoogleLogin",
            message: $"Customer '{customer.Email}' signed in with Google.",
            actorEmail: customer.Email,
            entityType: "CustomerUser",
            entityId: customer.Id.ToString(),
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        return Ok(BuildResponse(customer));
    }

    [HttpGet("me")]
    [Authorize(Roles = "Customer")]
    public ActionResult<object> Me()
    {
        return Ok(new
        {
            id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value,
            name = User.Identity?.Name,
        });
    }

    private CustomerAuthResponse BuildResponse(CustomerUser customer)
    {
        var token = tokenService.CreateCustomerToken(customer.Id, customer.Email, customer.Name);
        var expiresAt = DateTime.UtcNow.AddMinutes(jwtOptions.Value.ExpiresMinutes);
        return new CustomerAuthResponse(token, customer.Id, customer.Email, customer.Name, expiresAt);
    }
}
