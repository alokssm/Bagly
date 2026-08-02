using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Extensions;
using Bagly.Api.Mapping;
using Bagly.Api.Models;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/products")]
public class AdminProductsController(
    BaglyDbContext db,
    IAuditLogService auditLog,
    IStockAlertNotificationDispatcher stockAlertDispatcher) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AdminProductDto>>> GetAll(CancellationToken cancellationToken)
    {
        var products = await db.Products.AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(products.Select(ProductMapper.ToAdminDto));
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

    [HttpPost]
    public async Task<ActionResult<AdminProductDto>> Create(
        [FromBody] UpsertProductRequest request,
        CancellationToken cancellationToken)
    {
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

        var product = new Product
        {
            Id = id,
            CreatedAt = DateTime.UtcNow,
        };
        ProductMapper.ApplyUpsert(product, request);

        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "Product",
            action: "Create",
            message: $"Product '{product.Name}' created.",
            actorEmail: HttpContext.GetActorEmail(),
            entityType: "Product",
            entityId: product.Id,
            details: new { product.Name, product.Category, product.Price, product.IsActive },
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        if (product.IsAvailable)
        {
            stockAlertDispatcher.Enqueue(product.Id, "AdminProductCreate");
        }

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, ProductMapper.ToAdminDto(product));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AdminProductDto>> Update(
        string id,
        [FromBody] UpsertProductRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(request);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null) return NotFound(new { message = "Product not found." });

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
        ProductMapper.ApplyUpsert(product, request);
        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "Product",
            action: "Update",
            message: $"Product '{product.Id}' updated.",
            actorEmail: HttpContext.GetActorEmail(),
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

        // Restocked: product went from unavailable (inactive or out of stock) to available.
        if (!wasAvailable && product.IsAvailable)
        {
            stockAlertDispatcher.Enqueue(product.Id, "AdminProductUpdate");
        }

        return Ok(ProductMapper.ToAdminDto(product));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null) return NotFound(new { message = "Product not found." });

        var snapshot = new { product.Id, product.Name, product.Category, product.Price };
        db.Products.Remove(product);
        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "Product",
            action: "Delete",
            message: $"Product '{snapshot.Name}' deleted.",
            actorEmail: HttpContext.GetActorEmail(),
            entityType: "Product",
            entityId: id,
            details: snapshot,
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        return NoContent();
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
}
