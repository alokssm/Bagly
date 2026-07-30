using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Extensions;
using Bagly.Api.Options;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    BaglyDbContext db,
    IOptions<JwtOptions> jwtOptions,
    TokenService tokenService,
    IAuditLogService auditLog) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Email and password are required." });
        }

        var email = request.Email.Trim();
        var admin = await db.AdminUsers
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive, cancellationToken);

        if (admin is null || !PasswordHasher.Verify(request.Password, admin.PasswordHash))
        {
            await auditLog.LogAsync(
                category: "Auth",
                action: "LoginFailed",
                message: $"Failed admin login attempt for '{email}'.",
                level: "Warning",
                actorEmail: email,
                entityType: "AdminUser",
                ipAddress: HttpContext.GetClientIp(),
                requestPath: HttpContext.GetRequestPath(),
                cancellationToken: cancellationToken);

            return Unauthorized(new { message = "Invalid admin credentials." });
        }

        admin.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var token = tokenService.CreateAdminToken(admin.Email, admin.Name);
        var expiresAt = DateTime.UtcNow.AddMinutes(jwtOptions.Value.ExpiresMinutes);

        await auditLog.LogAsync(
            category: "Auth",
            action: "LoginSuccess",
            message: $"Admin '{admin.Email}' logged in.",
            actorEmail: admin.Email,
            entityType: "AdminUser",
            entityId: admin.Id.ToString(),
            details: new { admin.Name, admin.Role },
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        return Ok(new LoginResponse(token, admin.Email, admin.Name, admin.Role, expiresAt));
    }

    [HttpPost("logout")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var email = HttpContext.GetActorEmail();

        await auditLog.LogAsync(
            category: "Auth",
            action: "Logout",
            message: $"Admin '{email}' logged out.",
            actorEmail: email,
            entityType: "AdminUser",
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        return Ok(new { message = "Logged out." });
    }

    [HttpGet("me")]
    [Authorize(Roles = "Admin")]
    public ActionResult<object> Me()
    {
        return Ok(new
        {
            email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value,
            name = User.Identity?.Name,
            role = "Admin",
        });
    }
}
