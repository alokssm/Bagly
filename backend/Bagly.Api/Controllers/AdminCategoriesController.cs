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
[Route("api/admin/categories")]
public class AdminCategoriesController(BaglyDbContext db, IAuditLogService auditLog) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> GetAll(CancellationToken cancellationToken)
    {
        var categories = await db.Categories.AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .Select(c => new CategoryDto(c.Id, c.Label, c.SortOrder))
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(
        [FromBody] UpsertCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(request);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var id = ProductMapper.Slugify(request.Id);
        if (id == "all")
        {
            return BadRequest(new { message = "Category id 'all' is reserved." });
        }

        if (await db.Categories.AnyAsync(c => c.Id == id, cancellationToken))
        {
            return Conflict(new { message = $"Category '{id}' already exists." });
        }

        var category = new Category
        {
            Id = id,
            Label = request.Label.Trim(),
            SortOrder = request.SortOrder,
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "Category",
            action: "Create",
            message: $"Category '{category.Label}' created.",
            actorEmail: HttpContext.GetActorEmail(),
            entityType: "Category",
            entityId: category.Id,
            details: new { category.Id, category.Label, category.SortOrder },
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        return CreatedAtAction(nameof(GetAll), new CategoryDto(category.Id, category.Label, category.SortOrder));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<CategoryDto>> Update(
        string id,
        [FromBody] UpsertCategoryRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Label))
        {
            return BadRequest(new { message = "Label is required." });
        }

        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null) return NotFound(new { message = "Category not found." });

        var before = new { category.Label, category.SortOrder };
        category.Label = request.Label.Trim();
        category.SortOrder = request.SortOrder;

        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "Category",
            action: "Update",
            message: $"Category '{category.Id}' updated.",
            actorEmail: HttpContext.GetActorEmail(),
            entityType: "Category",
            entityId: category.Id,
            details: new
            {
                before,
                after = new { category.Label, category.SortOrder },
            },
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        return Ok(new CategoryDto(category.Id, category.Label, category.SortOrder));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        if (string.Equals(id, "all", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "The 'all' category cannot be deleted." });
        }

        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null) return NotFound(new { message = "Category not found." });

        var inUse = await db.Products.AnyAsync(p => p.Category == id, cancellationToken);
        if (inUse)
        {
            return BadRequest(new { message = "Cannot delete a category that still has products." });
        }

        var snapshot = new { category.Id, category.Label, category.SortOrder };
        db.Categories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);

        await auditLog.LogAsync(
            category: "Category",
            action: "Delete",
            message: $"Category '{snapshot.Label}' deleted.",
            actorEmail: HttpContext.GetActorEmail(),
            entityType: "Category",
            entityId: id,
            details: snapshot,
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        return NoContent();
    }

    private static string? Validate(UpsertCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Id)) return "Id is required.";
        if (string.IsNullOrWhiteSpace(request.Label)) return "Label is required.";
        return null;
    }
}
