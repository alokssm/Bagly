using Bagly.Api.Data;
using Bagly.Api.Models;
using Bagly.Api.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

/// <summary>Seller-facing Shiprocket helpers (seller-owned nicknames — no credentials).</summary>
[ApiController]
[Route("api/seller/shiprocket")]
[Authorize(Roles = "Seller")]
public class SellerShiprocketController(
    BaglyDbContext db,
    ISellerPickupService pickupService) : ControllerBase
{
    /// <summary>
    /// Seller-owned pickup nicknames only (from <c>SellerPickupLocations</c>).
    /// Does not include platform config defaults such as global home/work.
    /// Prefer <c>GET /api/seller/pickups</c> for full address details on the product form.
    /// </summary>
    [HttpGet("pickup-locations")]
    public async Task<ActionResult<object>> GetConfiguredPickupLocations(CancellationToken cancellationToken)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null) return Unauthorized(new { message = "Seller session is invalid." });

        if (!string.Equals(seller.Status, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Your seller account must be approved before you can list pickup locations.",
            });
        }

        var locations = (await pickupService.ListNicknamesAsync(seller.Id, cancellationToken))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(new { locations });
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
