using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Mapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController(BaglyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts(
        [FromQuery] string? category,
        [FromQuery] string? subCategory,
        [FromQuery] string? sort,
        CancellationToken cancellationToken)
    {
        var query = db.Products.AsNoTracking().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(category) &&
            !string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => p.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(subCategory) &&
            !string.Equals(subCategory, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => p.SubCategoryId == subCategory);
        }

        var products = await query.ToListAsync(cancellationToken);

        var mapped = products.Select(ProductMapper.ToDto).ToList();

        mapped = sort?.ToLowerInvariant() switch
        {
            "price-asc" => mapped.OrderBy(p => p.Price).ToList(),
            "price-desc" => mapped.OrderByDescending(p => p.Price).ToList(),
            "name" => mapped.OrderBy(p => p.Name).ToList(),
            _ => mapped,
        };

        return Ok(mapped);
    }

    /// <summary>Looks up by <c>Id</c> first (legacy/bookmarked links keep working), then falls back
    /// to <c>Slug</c> so SEO-friendly product URLs (e.g. <c>/product/leather-tote-bag</c>) resolve too.</summary>
    [HttpGet("{idOrSlug}")]
    public async Task<ActionResult<ProductDto>> GetProduct(string idOrSlug, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == idOrSlug && p.IsActive, cancellationToken);

        product ??= await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == idOrSlug && p.IsActive, cancellationToken);

        if (product is null)
        {
            return NotFound(new { message = $"Product '{idOrSlug}' was not found." });
        }

        return Ok(ProductMapper.ToDto(product));
    }
}
