using Bagly.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bagly.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/uploads")]
public class AdminUploadsController(ICloudinaryImageService cloudinaryImageService) : ControllerBase
{
    // Cloudinary's free tier accepts much larger files, but product photos never need to be this big
    // and keeping the limit small avoids burning through free-tier storage/bandwidth quickly.
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
}
