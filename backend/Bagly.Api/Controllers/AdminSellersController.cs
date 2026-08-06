using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Extensions;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

[ApiController]
[Route("api/admin/sellers")]
[Authorize(Roles = "Admin")]
public class AdminSellersController(
    BaglyDbContext db,
    ISellerApprovalEmailService sellerApprovalEmail,
    IAuditLogService auditLog) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminSellerListItemDto>>> GetSellers(
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.SellerUsers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim();
            query = query.Where(s => s.Status == normalized);
        }

        var items = await query
            .OrderByDescending(s => s.ProfileSubmittedAt ?? s.CreatedAt)
            .Select(s => new AdminSellerListItemDto(
                s.Id,
                s.Email,
                s.Name,
                s.BusinessName,
                s.Phone,
                s.City,
                s.State,
                s.Gstin,
                s.Status,
                s.RejectionReason,
                s.ProfileSubmittedAt != null
                    && s.Phone != null && s.Phone != ""
                    && s.AddressLine1 != null && s.AddressLine1 != ""
                    && s.City != null && s.City != ""
                    && s.State != null && s.State != ""
                    && s.Pincode != null && s.Pincode != "",
                s.CreatedAt,
                s.ProfileSubmittedAt,
                s.ApprovedAt))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminSellerDetailDto>> GetSeller(
        Guid id,
        CancellationToken cancellationToken)
    {
        var seller = await db.SellerUsers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (seller is null)
        {
            return NotFound(new { message = "Seller not found." });
        }

        return Ok(ToDetail(seller));
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<AdminSellerDetailDto>> Approve(
        Guid id,
        CancellationToken cancellationToken)
    {
        var seller = await db.SellerUsers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (seller is null)
        {
            return NotFound(new { message = "Seller not found." });
        }

        if (seller.Status == "Approved")
        {
            return Ok(ToDetail(seller));
        }

        seller.Status = "Approved";
        seller.ApprovedAt = DateTime.UtcNow;
        seller.IsActive = true;
        seller.RejectionReason = null;
        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "SellerAdmin",
            action: "Approve",
            message: $"Seller '{seller.Email}' ({seller.BusinessName}) approved.",
            actorEmail: User.Identity?.Name,
            entityType: "SellerUser",
            entityId: seller.Id.ToString(),
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        // Best-effort — approval must succeed even if email fails.
        try
        {
            await sellerApprovalEmail.SendApprovedAsync(seller, cancellationToken);
        }
        catch (Exception)
        {
            // Logged inside the email service; do not fail the request.
        }

        return Ok(ToDetail(seller));
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<AdminSellerDetailDto>> Reject(
        Guid id,
        [FromBody] RejectSellerRequest? request,
        CancellationToken cancellationToken)
    {
        var seller = await db.SellerUsers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (seller is null)
        {
            return NotFound(new { message = "Seller not found." });
        }

        var reason = string.IsNullOrWhiteSpace(request?.Reason)
            ? null
            : request.Reason.Trim();

        if (reason is { Length: > 500 })
        {
            return BadRequest(new { message = "Rejection reason must be 500 characters or fewer." });
        }

        seller.Status = "Rejected";
        seller.RejectionReason = reason;
        seller.ApprovedAt = null;
        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "SellerAdmin",
            action: "Reject",
            message: $"Seller '{seller.Email}' ({seller.BusinessName}) rejected.",
            actorEmail: User.Identity?.Name,
            entityType: "SellerUser",
            entityId: seller.Id.ToString(),
            details: reason is null ? null : new { reason },
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        try
        {
            await sellerApprovalEmail.SendRejectedAsync(seller, reason, cancellationToken);
        }
        catch (Exception)
        {
            // Best-effort email.
        }

        return Ok(ToDetail(seller));
    }

    private static AdminSellerDetailDto ToDetail(Models.SellerUser s) =>
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
            s.IsActive,
            IsProfileComplete(s),
            s.CreatedAt,
            s.LastLoginAt,
            s.ProfileSubmittedAt,
            s.ApprovedAt);

    private static bool IsProfileComplete(Models.SellerUser s) =>
        s.ProfileSubmittedAt != null &&
        !string.IsNullOrWhiteSpace(s.Phone) &&
        !string.IsNullOrWhiteSpace(s.AddressLine1) &&
        !string.IsNullOrWhiteSpace(s.City) &&
        !string.IsNullOrWhiteSpace(s.State) &&
        !string.IsNullOrWhiteSpace(s.Pincode);
}
