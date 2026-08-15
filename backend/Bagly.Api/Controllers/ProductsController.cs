using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Mapping;
using Bagly.Api.Models.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bagly.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController(BaglyDbContext db) : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductDto>>> GetProducts(
        [FromQuery] string? category,
        [FromQuery] string? subCategory,
        [FromQuery] string? sort,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePaging(page, pageSize);

        var (items, totalCount) = await LoadActiveProductsPageAsync(
            category, subCategory, q, sort, page, pageSize, cancellationToken);

        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        var mapped = items.Select(ProductMapper.ToDto).ToList();

        return Ok(new PagedResult<ProductDto>(mapped, page, pageSize, totalCount, totalPages));
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

    private async Task<(List<Models.Product> Items, int TotalCount)> LoadActiveProductsPageAsync(
        string? category,
        string? subCategory,
        string? q,
        string? sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        async Task<(List<Models.Product> Items, int TotalCount)> Query()
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

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(p =>
                    p.Name.ToLower().Contains(term) ||
                    p.ShortDescription.ToLower().Contains(term) ||
                    p.Description.ToLower().Contains(term) ||
                    p.Category.ToLower().Contains(term) ||
                    (p.Slug != null && p.Slug.ToLower().Contains(term)));
            }

            var totalCount = await query.CountAsync(cancellationToken);

            query = sort?.ToLowerInvariant() switch
            {
                "price-asc" => query.OrderBy(p => p.Price).ThenBy(p => p.Id),
                "price-desc" => query.OrderByDescending(p => p.Price).ThenBy(p => p.Id),
                "name" => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
                _ => query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id),
            };

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        return await RunWithSchemaSelfHealAsync(Query, cancellationToken);
    }

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (page, pageSize);
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

    /// <summary>Postgres SQLSTATE 42703 is "undefined_column" — exactly what Npgsql/EF Core throws
    /// when the mapped entity has a property (e.g. Product.Slug) that doesn't exist as a column yet.</summary>
    private static bool IsMissingColumnError(Exception ex) =>
        (ex.GetBaseException() as PostgresException)?.SqlState == PostgresErrorCodes.UndefinedColumn;
}
