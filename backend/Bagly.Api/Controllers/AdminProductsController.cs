using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Mapping;
using Bagly.Api.Models.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

/// <summary>
/// Admin product access is read-only. Marketplace sellers own create/update/delete
/// via <c>api/seller/products</c>. Categories remain admin-managed.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/products")]
public class AdminProductsController(BaglyDbContext db) : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminProductListItemDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        [FromQuery] string? subCategory = null,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePaging(page, pageSize);

        var query = db.Products.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category.Trim());

        if (!string.IsNullOrWhiteSpace(subCategory))
            query = query.Where(p => p.SubCategoryId == subCategory.Trim());

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                p.Name.Contains(term) ||
                p.Id.Contains(term) ||
                db.Categories.Any(c => c.Id == p.Category && c.Label.Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new AdminProductListItemDto(
                p.Id,
                p.Name,
                p.Category,
                p.SubCategoryId,
                p.Price,
                p.StockQuantity,
                p.Image,
                p.IsActive,
                p.IsActive && p.StockQuantity > 0,
                p.CreatedAt,
                p.SellerId,
                p.ShiprocketPickupLocation))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<AdminProductListItemDto>(items, page, pageSize, totalCount, totalPages));
    }

    [HttpGet("stats")]
    public async Task<ActionResult<ProductStatsDto>> GetStats(CancellationToken cancellationToken)
    {
        var totalCount = await db.Products.CountAsync(cancellationToken);
        var activeCount = await db.Products.CountAsync(p => p.IsActive && p.StockQuantity > 0, cancellationToken);
        return Ok(new ProductStatsDto(totalCount, activeCount));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AdminProductDto>> GetById(string id, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return product is null
            ? NotFound(new { message = "Product not found." })
            : Ok(ProductMapper.ToAdminDto(product));
    }

    /// <summary>
    /// Admin-only: set Shiprocket pickup nickname on a product (platform or seller catalog)
    /// without reopening full product CRUD.
    /// Product ids are string catalog keys (e.g. st-001), not GUIDs.
    /// Accepts PATCH and PUT (some proxies are picky about PATCH alone).
    /// </summary>
    [AcceptVerbs("PATCH", "PUT")]
    [Route("{id}/pickup-location")]
    public async Task<ActionResult<AdminProductDto>> PatchPickupLocation(
        string id,
        [FromBody] PatchProductPickupLocationRequest? request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { message = "Product id is required." });
        }

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return NotFound(new { message = "Product not found." });
        }

        product.ShiprocketPickupLocation = ProductMapper.NormalizePickupNickname(request?.ShiprocketPickupLocation);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ProductMapper.ToAdminDto(product));
    }

    /// <summary>Product catalog writes are seller-owned. Admins manage categories only.</summary>
    [HttpPost]
    public ActionResult Create() =>
        StatusCode(StatusCodes.Status403Forbidden, new
        {
            message = "Admins cannot create products. Approved sellers manage their own catalog.",
        });

    [HttpPut("{id}")]
    public ActionResult Update(string id) =>
        StatusCode(StatusCodes.Status403Forbidden, new
        {
            message = "Admins cannot update products. Approved sellers manage their own catalog.",
        });

    [HttpDelete("{id}")]
    public ActionResult Delete(string id) =>
        StatusCode(StatusCodes.Status403Forbidden, new
        {
            message = "Admins cannot delete products. Approved sellers manage their own catalog.",
        });

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (page, pageSize);
    }
}
