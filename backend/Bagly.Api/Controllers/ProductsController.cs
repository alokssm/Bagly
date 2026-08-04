using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Mapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
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
        var products = await LoadActiveProductsAsync(category, subCategory, cancellationToken);

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
        var product = await RunWithSchemaSelfHealAsync(
            () => db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == idOrSlug && p.IsActive, cancellationToken),
            cancellationToken);

        product ??= await RunWithSchemaSelfHealAsync(
            () => db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Slug == idOrSlug && p.IsActive, cancellationToken),
            cancellationToken);

        if (product is null)
        {
            return NotFound(new { message = $"Product '{idOrSlug}' was not found." });
        }

        return Ok(ProductMapper.ToDto(product));
    }

    private async Task<List<Models.Product>> LoadActiveProductsAsync(
        string? category,
        string? subCategory,
        CancellationToken cancellationToken)
    {
        Task<List<Models.Product>> Query()
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

            return query.ToListAsync(cancellationToken);
        }

        return await RunWithSchemaSelfHealAsync(Query, cancellationToken);
    }

    /// <summary>Runs a Products query and, if it fails because a column the current code expects
    /// (e.g. Slug/SeoTitle, SubCategoryId) is unexpectedly missing on the database — for instance
    /// when a request lands while startup schema bootstrap is still catching up on Render/Azure —
    /// re-runs the Products schema fixups and retries the query once instead of surfacing a 500 to
    /// the storefront.</summary>
    private async Task<T> RunWithSchemaSelfHealAsync<T>(Func<Task<T>> query, CancellationToken cancellationToken)
    {
        try
        {
            return await query();
        }
        catch (Exception ex) when (IsMissingColumnError(ex))
        {
            await DatabaseBootstrapper.EnsureProductsSchemaAsync(db, cancellationToken);
            return await query();
        }
    }

    /// <summary>SQL Server error 207 is "Invalid column name" — exactly what EF Core throws when
    /// the mapped entity has a property (e.g. Product.Slug) that doesn't exist as a column yet.</summary>
    private static bool IsMissingColumnError(Exception ex) =>
        (ex.GetBaseException() as SqlException)?.Number == 207;
}
