using Bagly.Api.Data;
using Bagly.Api.Models;
using Bagly.Api.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

/// <summary>
/// Approved sellers manage their Shiprocket pickup addresses (max 2).
/// Creates call Shiprocket addpickup; Bagly persists only on success.
/// </summary>
[ApiController]
[Authorize(Roles = "Seller")]
[Route("api/seller/pickups")]
public class SellerPickupsController(
    BaglyDbContext db,
    ISellerPickupService pickupService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SellerPickupListResponse>> List(CancellationToken cancellationToken)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null) return Unauthorized(new { message = "Seller session is invalid." });

        var approvalError = RequireApproved(seller);
        if (approvalError is not null) return approvalError;

        var result = await pickupService.ListAsync(seller.Id, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<SellerPickupLocationDto>> Create(
        [FromBody] CreateSellerPickupRequest request,
        CancellationToken cancellationToken)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null) return Unauthorized(new { message = "Seller session is invalid." });

        var approvalError = RequireApproved(seller);
        if (approvalError is not null) return approvalError;

        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        try
        {
            var created = await pickupService.CreateAsync(seller, request, cancellationToken);
            return Ok(created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Max-2 and Shiprocket failures surface as 400 with the message.
            return BadRequest(new { message = ex.Message });
        }
    }

    private static ActionResult? RequireApproved(SellerUser seller)
    {
        if (string.Equals(seller.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            return null;

        return new ObjectResult(new
        {
            message = "Your seller account must be approved before you can manage pickup locations.",
        })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }

    private async Task<SellerUser?> LoadCurrentSellerAsync(CancellationToken cancellationToken)
    {
        var idClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(idClaim, out var sellerId))
            return null;

        return await db.SellerUsers
            .FirstOrDefaultAsync(u => u.Id == sellerId && u.IsActive, cancellationToken);
    }
}
