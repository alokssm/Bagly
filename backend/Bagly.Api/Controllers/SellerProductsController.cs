using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Extensions;
using Bagly.Api.Mapping;
using Bagly.Api.Models;
using Bagly.Api.Models.Dtos;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

/// <summary>
/// Approved sellers manage only their own products (<see cref="Product.SellerId"/>).
/// Platform/legacy catalog rows (null SellerId) are not writable here.
/// </summary>
[ApiController]
[Authorize(Roles = "Seller")]
[Route("api/seller/products")]
public class SellerProductsController(
    BaglyDbContext db,
    IAuditLogService auditLog,
    IStockAlertNotificationDispatcher stockAlertDispatcher) : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminProductListItemDto>>> GetMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null) return Unauthorized(new { message = "Seller session is invalid." });

        var approvalError = RequireApproved(seller);
        if (approvalError is not null) return approvalError;

        (page, pageSize) = NormalizePaging(page, pageSize);

        var query = db.Products.AsNoTracking().Where(p => p.SellerId == seller.Id);

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

    [HttpGet("{id}")]
    public async Task<ActionResult<AdminProductDto>> GetById(string id, CancellationToken cancellationToken)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null) return Unauthorized(new { message = "Seller session is invalid." });

        var approvalError = RequireApproved(seller);
        if (approvalError is not null) return approvalError;

        var product = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.SellerId == seller.Id, cancellationToken);

        return product is null
            ? NotFound(new { message = "Product not found." })
            : Ok(ProductMapper.ToAdminDto(product));
    }

    [HttpPost]
    public async Task<ActionResult<AdminProductDto>> Create(
        [FromBody] UpsertProductRequest request,
        CancellationToken cancellationToken)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null) return Unauthorized(new { message = "Seller session is invalid." });

        var approvalError = RequireApproved(seller);
        if (approvalError is not null) return approvalError;

        var validationError = Validate(request);
        if (validationError is not null) return BadRequest(new { message = validationError });

        if (!await db.Categories.AnyAsync(c => c.Id == request.Category && c.Id != "all", cancellationToken))
        {
            return BadRequest(new { message = $"Category '{request.Category}' does not exist." });
        }

        if (!string.IsNullOrWhiteSpace(request.SubCategoryId) &&
            !await db.Categories.AnyAsync(c => c.Id == request.SubCategoryId, cancellationToken))
        {
            return BadRequest(new { message = $"Subcategory '{request.SubCategoryId}' does not exist." });
        }

        var id = string.IsNullOrWhiteSpace(request.Id)
            ? ProductMapper.Slugify(request.Name)
            : ProductMapper.Slugify(request.Id);

        if (await db.Products.AnyAsync(p => p.Id == id, cancellationToken))
        {
            id = $"{id}-{Guid.NewGuid():N}"[..Math.Min(100, id.Length + 9)];
        }

        var slugBase = ProductMapper.Slugify(string.IsNullOrWhiteSpace(request.Slug) ? request.Name : request.Slug);
        var slug = await GenerateUniqueSlugAsync(slugBase, excludeId: null, cancellationToken);

        var product = new Product
        {
            Id = id,
            Slug = slug,
            SellerId = seller.Id,
            CreatedAt = DateTime.UtcNow,
        };
        ProductMapper.ApplyUpsert(product, request);

        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "Product",
            action: "SellerCreate",
            message: $"Seller '{seller.Email}' created product '{product.Name}'.",
            actorEmail: seller.Email,
            entityType: "Product",
            entityId: product.Id,
            details: new { product.Name, product.Category, product.Price, product.IsActive, product.SellerId },
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        if (product.IsAvailable)
        {
            stockAlertDispatcher.Enqueue(product.Id, "SellerProductCreate");
        }

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, ProductMapper.ToAdminDto(product));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AdminProductDto>> Update(
        string id,
        [FromBody] UpsertProductRequest request,
        CancellationToken cancellationToken)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null) return Unauthorized(new { message = "Seller session is invalid." });

        var approvalError = RequireApproved(seller);
        if (approvalError is not null) return approvalError;

        var validationError = Validate(request);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null) return NotFound(new { message = "Product not found." });
        if (product.SellerId != seller.Id)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "You can only update your own products.",
            });
        }

        if (!await db.Categories.AnyAsync(c => c.Id == request.Category && c.Id != "all", cancellationToken))
        {
            return BadRequest(new { message = $"Category '{request.Category}' does not exist." });
        }

        if (!string.IsNullOrWhiteSpace(request.SubCategoryId) &&
            !await db.Categories.AnyAsync(c => c.Id == request.SubCategoryId, cancellationToken))
        {
            return BadRequest(new { message = $"Subcategory '{request.SubCategoryId}' does not exist." });
        }

        var before = new { product.Name, product.Category, product.Price, product.IsActive };
        var wasAvailable = product.IsAvailable;

        var requestedSlugBase = ProductMapper.Slugify(
            string.IsNullOrWhiteSpace(request.Slug)
                ? (string.IsNullOrWhiteSpace(product.Slug) ? request.Name : product.Slug)
                : request.Slug);
        if (!string.Equals(requestedSlugBase, product.Slug, StringComparison.Ordinal))
        {
            product.Slug = await GenerateUniqueSlugAsync(requestedSlugBase, product.Id, cancellationToken);
        }

        ProductMapper.ApplyUpsert(product, request);
        // Ownership is immutable for seller products.
        product.SellerId = seller.Id;
        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "Product",
            action: "SellerUpdate",
            message: $"Seller '{seller.Email}' updated product '{product.Id}'.",
            actorEmail: seller.Email,
            entityType: "Product",
            entityId: product.Id,
            details: new
            {
                before,
                after = new { product.Name, product.Category, product.Price, product.IsActive },
            },
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        if (!wasAvailable && product.IsAvailable)
        {
            stockAlertDispatcher.Enqueue(product.Id, "SellerProductUpdate");
        }

        return Ok(ProductMapper.ToAdminDto(product));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var seller = await LoadCurrentSellerAsync(cancellationToken);
        if (seller is null) return Unauthorized(new { message = "Seller session is invalid." });

        var approvalError = RequireApproved(seller);
        if (approvalError is not null) return approvalError;

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null) return NotFound(new { message = "Product not found." });
        if (product.SellerId != seller.Id)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "You can only delete your own products.",
            });
        }

        var snapshot = new { product.Id, product.Name, product.Category, product.Price };
        db.Products.Remove(product);
        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "Product",
            action: "SellerDelete",
            message: $"Seller '{seller.Email}' deleted product '{snapshot.Name}'.",
            actorEmail: seller.Email,
            entityType: "Product",
            entityId: id,
            details: snapshot,
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        return NoContent();
    }

    private ActionResult? RequireApproved(SellerUser seller)
    {
        if (string.Equals(seller.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            return null;

        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            message = "Your seller account must be approved before you can manage products.",
        });
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

    private static string? Validate(UpsertProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return "Name is required.";
        if (string.IsNullOrWhiteSpace(request.Category)) return "Category is required.";
        if (request.Price < 0) return "Price must be zero or greater.";
        if (request.StockQuantity < 0) return "Stock quantity must be zero or greater.";
        if (string.IsNullOrWhiteSpace(request.Image)) return "Image URL is required.";
        return null;
    }

    private async Task<string> GenerateUniqueSlugAsync(string baseSlug, string? excludeId, CancellationToken cancellationToken)
    {
        var candidate = baseSlug;
        var suffix = 2;
        while (await db.Products.AnyAsync(p => p.Slug == candidate && p.Id != excludeId, cancellationToken))
        {
            candidate = $"{baseSlug}-{suffix}";
            suffix++;
        }
        return candidate;
    }

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (page, pageSize);
    }
}
