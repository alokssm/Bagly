using Bagly.Api.Data;
using Bagly.Api.Models;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

/// <summary>Cloudinary image upload for approved sellers (same limits as admin uploads).</summary>
[ApiController]
[Authorize(Roles = "Seller")]
[Route("api/seller/uploads")]
public class SellerUploadsController(
    BaglyDbContext db,
    ICloudinaryImageService cloudinaryImageService) : ControllerBase
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
    };

    [HttpPost("image")]
    [RequestSizeLimit(MaxFileSizeBytes + 1024)]
    public async Task<IActionResult> UploadImage(IFormFile? file, CancellationToken cancellationToken)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null) return Unauthorized(new { message = "Seller session is invalid." });

        if (!string.Equals(seller.Status, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Your seller account must be approved before you can upload images.",
            });
        }

        if (!cloudinaryImageService.IsConfigured)
        {
            return StatusCode(503, new
            {
                message = "Image uploads are not configured. Set Cloudinary__CloudName, Cloudinary__ApiKey, and Cloudinary__ApiSecret, then redeploy.",
            });
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "No file was uploaded." });
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest(new { message = "Image must be 5 MB or smaller." });
        }

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            return BadRequest(new { message = "Only JPEG, PNG, WEBP, and GIF images are allowed." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var url = await cloudinaryImageService.UploadImageAsync(stream, file.FileName, cancellationToken);
            return Ok(new { url });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { message = $"Upload to Cloudinary failed: {ex.Message}" });
        }
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
