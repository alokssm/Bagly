using System.Security.Claims;
using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

[ApiController]
[Route("api/products/{productId}/reviews")]
public class ProductReviewsController(BaglyDbContext db) : ControllerBase
{
    private const string ConfirmedStatus = "Confirmed";
    private const int MinRating = 1;
    private const int MaxRating = 5;
    private const int MaxCommentLength = 2000;

    /// <summary>Public review list with average/count. When a customer JWT is present, also returns canReview / hasReviewed / myReview.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ProductReviewsResponse>> GetReviews(
        string productId,
        CancellationToken cancellationToken)
    {
        var product = await ResolveProductAsync(productId, cancellationToken);
        if (product is null)
        {
            return NotFound(new { message = $"Product '{productId}' was not found." });
        }

        var reviews = await db.ProductReviews.AsNoTracking()
            .Where(r => r.ProductId == product.Id)
            .Include(r => r.CustomerUser)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        var customerId = GetCustomerId();
        var (average, count) = Aggregate(reviews);

        ProductReviewDto? myReview = null;
        bool? canReview = null;
        bool? hasReviewed = null;

        if (customerId is not null)
        {
            var mine = reviews.FirstOrDefault(r => r.CustomerUserId == customerId.Value);
            hasReviewed = mine is not null;
            myReview = mine is null ? null : ToDto(mine, isMine: true);
            if (mine is null)
            {
                var email = await GetCustomerEmailAsync(customerId.Value, cancellationToken);
                canReview = email is not null
                    && await HasPurchasedAsync(product.Id, customerId.Value, email, cancellationToken);
            }
            else
            {
                canReview = false;
            }
        }

        var mapped = reviews
            .Select(r => ToDto(r, isMine: customerId is not null && r.CustomerUserId == customerId.Value))
            .ToList();

        return Ok(new ProductReviewsResponse(
            product.Id,
            average,
            count,
            mapped,
            canReview,
            hasReviewed,
            myReview));
    }

    [HttpPost]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult<ProductReviewDto>> CreateReview(
        string productId,
        [FromBody] CreateProductReviewRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        if (customerId is null)
        {
            return Unauthorized();
        }

        var product = await ResolveProductAsync(productId, cancellationToken);
        if (product is null)
        {
            return NotFound(new { message = $"Product '{productId}' was not found." });
        }

        var validation = ValidateRatingAndComment(request.Rating, request.Comment);
        if (validation is not null)
        {
            return BadRequest(new { message = validation });
        }

        var alreadyReviewed = await db.ProductReviews
            .AnyAsync(r => r.ProductId == product.Id && r.CustomerUserId == customerId.Value, cancellationToken);
        if (alreadyReviewed)
        {
            return Conflict(new { message = "You have already reviewed this product." });
        }

        var customer = await db.CustomerUsers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId.Value && c.IsActive, cancellationToken);
        if (customer is null)
        {
            return Unauthorized();
        }

        if (!await HasPurchasedAsync(product.Id, customer.Id, customer.Email, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only review products you have purchased." });
        }

        var now = DateTime.UtcNow;
        var review = new ProductReview
        {
            ProductId = product.Id,
            CustomerUserId = customerId.Value,
            Rating = request.Rating,
            Comment = NormalizeComment(request.Comment),
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.ProductReviews.Add(review);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "You have already reviewed this product." });
        }

        await SyncProductAggregatesAsync(product.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        review.CustomerUser = customer;
        return CreatedAtAction(nameof(GetReviews), new { productId = product.Id }, ToDto(review, isMine: true));
    }

    [HttpPut("me")]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult<ProductReviewDto>> UpdateMyReview(
        string productId,
        [FromBody] UpdateProductReviewRequest request,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        if (customerId is null)
        {
            return Unauthorized();
        }

        var product = await ResolveProductAsync(productId, cancellationToken);
        if (product is null)
        {
            return NotFound(new { message = $"Product '{productId}' was not found." });
        }

        var validation = ValidateRatingAndComment(request.Rating, request.Comment);
        if (validation is not null)
        {
            return BadRequest(new { message = validation });
        }

        var review = await db.ProductReviews
            .Include(r => r.CustomerUser)
            .FirstOrDefaultAsync(
                r => r.ProductId == product.Id && r.CustomerUserId == customerId.Value,
                cancellationToken);

        if (review is null)
        {
            return NotFound(new { message = "You have not reviewed this product." });
        }

        review.Rating = request.Rating;
        review.Comment = NormalizeComment(request.Comment);
        review.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await SyncProductAggregatesAsync(product.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(review, isMine: true));
    }

    [HttpDelete("me")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> DeleteMyReview(string productId, CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId();
        if (customerId is null)
        {
            return Unauthorized();
        }

        var product = await ResolveProductAsync(productId, cancellationToken);
        if (product is null)
        {
            return NotFound(new { message = $"Product '{productId}' was not found." });
        }

        var review = await db.ProductReviews
            .FirstOrDefaultAsync(
                r => r.ProductId == product.Id && r.CustomerUserId == customerId.Value,
                cancellationToken);

        if (review is null)
        {
            return NotFound(new { message = "You have not reviewed this product." });
        }

        db.ProductReviews.Remove(review);
        await db.SaveChangesAsync(cancellationToken);
        await SyncProductAggregatesAsync(product.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task SyncProductAggregatesAsync(string productId, CancellationToken cancellationToken)
    {
        var ratings = await db.ProductReviews
            .Where(r => r.ProductId == productId)
            .Select(r => r.Rating)
            .ToListAsync(cancellationToken);

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            return;
        }

        if (ratings.Count == 0)
        {
            product.Rating = 0;
            product.Reviews = 0;
        }
        else
        {
            product.Rating = Math.Round(ratings.Average(), 1);
            product.Reviews = ratings.Count;
        }
    }

    /// <summary>
    /// Purchase gate matches analytics revenue (Status == Confirmed). Also accepts older guest
    /// checkouts linked by the account email, same as AccountOrdersController.
    /// </summary>
    private async Task<bool> HasPurchasedAsync(
        string productId,
        Guid customerId,
        string email,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return await db.OrderItems.AsNoTracking()
            .AnyAsync(
                i => i.ProductId == productId
                     && i.Order!.Status == ConfirmedStatus
                     && (i.Order.CustomerUserId == customerId
                         || i.Order.Email.ToLower() == normalizedEmail),
                cancellationToken);
    }

    private async Task<string?> GetCustomerEmailAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var email = await db.CustomerUsers.AsNoTracking()
            .Where(c => c.Id == customerId && c.IsActive)
            .Select(c => c.Email)
            .FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(email) ? null : email;
    }

    private async Task<Product?> ResolveProductAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == idOrSlug && p.IsActive, cancellationToken);

        product ??= await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == idOrSlug && p.IsActive, cancellationToken);

        return product;
    }

    private Guid? GetCustomerId()
    {
        if (User.Identity?.IsAuthenticated != true || !User.IsInRole("Customer"))
        {
            return null;
        }

        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static (double Average, int Count) Aggregate(IReadOnlyList<ProductReview> reviews)
    {
        if (reviews.Count == 0)
        {
            return (0, 0);
        }

        return (Math.Round(reviews.Average(r => r.Rating), 1), reviews.Count);
    }

    private static string? ValidateRatingAndComment(int rating, string? comment)
    {
        if (rating < MinRating || rating > MaxRating)
        {
            return $"Rating must be between {MinRating} and {MaxRating}.";
        }

        if (comment is not null && comment.Trim().Length > MaxCommentLength)
        {
            return $"Comment must be at most {MaxCommentLength} characters.";
        }

        return null;
    }

    private static string? NormalizeComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        return comment.Trim();
    }

    private static ProductReviewDto ToDto(ProductReview review, bool isMine) =>
        new(
            review.Id,
            review.ProductId,
            review.CustomerUserId,
            ToReviewerName(review.CustomerUser?.Name),
            review.Rating,
            review.Comment,
            review.CreatedAt,
            review.UpdatedAt,
            isMine);

    private static string ToReviewerName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return "Customer";
        }

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "Customer" : parts[0];
    }
}
