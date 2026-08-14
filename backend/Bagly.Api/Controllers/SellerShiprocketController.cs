using Bagly.Api.Data;
using Bagly.Api.Models;
using Bagly.Api.Options;
using Bagly.Api.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Controllers;

/// <summary>Seller-facing Shiprocket helpers (config + seller-owned nicknames — no credentials).</summary>
[ApiController]
[Route("api/seller/shiprocket")]
[Authorize(Roles = "Seller")]
public class SellerShiprocketController(
    BaglyDbContext db,
    IOptions<ShiprocketOptions> options,
    ISellerPickupService pickupService) : ControllerBase
{
    /// <summary>
    /// Pickup nicknames for the product form dropdown:
    /// configured <c>Shiprocket:PickupLocations</c> plus this seller's saved nicknames.
    /// </summary>
    [HttpGet("pickup-locations")]
    public async Task<ActionResult<object>> GetConfiguredPickupLocations(CancellationToken cancellationToken)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null) return Unauthorized(new { message = "Seller session is invalid." });

        var set = new HashSet<string>(options.Value.GetPickupLocationChoices(), StringComparer.Ordinal);
        if (string.Equals(seller.Status, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var nick in await pickupService.ListNicknamesAsync(seller.Id, cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(nick))
                    set.Add(nick.Trim());
            }
        }

        var locations = set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
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
