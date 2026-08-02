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

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/categories")]
public class AdminCategoriesController(BaglyDbContext db, IAuditLogService auditLog) : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminCategoryDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePaging(page, pageSize);

        var query = db.Categories.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c => c.Label.Contains(term) || c.Id.Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var pageItems = await query
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Label)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new { c.Id, c.Label, c.SortOrder, c.IsActive, c.ParentId })
            .ToListAsync(cancellationToken);

        // Batch-resolve parent labels in one extra query instead of per-row lookups (no N+1).
        var parentIds = pageItems.Where(c => c.ParentId != null).Select(c => c.ParentId!).Distinct().ToList();
        var parentLabels = parentIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.Categories.AsNoTracking()
                .Where(c => parentIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Label, cancellationToken);

        var items = pageItems
            .Select(c => new AdminCategoryDto(
                c.Id,
                c.Label,
                c.SortOrder,
                c.IsActive,
                c.ParentId,
                c.ParentId != null && parentLabels.TryGetValue(c.ParentId, out var parentLabel) ? parentLabel : null))
            .ToList();

        return Ok(new PagedResult<AdminCategoryDto>(items, page, pageSize, totalCount, totalPages));
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

        var parentId = string.IsNullOrWhiteSpace(request.ParentId) ? null : request.ParentId.Trim();
        if (parentId is not null && !await db.Categories.AnyAsync(c => c.Id == parentId, cancellationToken))
        {
            return BadRequest(new { message = $"Parent category '{parentId}' does not exist." });
        }

        var category = new Category
        {
            Id = id,
            Label = request.Label.Trim(),
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            ParentId = parentId,
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
            details: new { category.Id, category.Label, category.SortOrder, category.IsActive, category.ParentId },
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        return CreatedAtAction(
            nameof(GetAll),
            new CategoryDto(category.Id, category.Label, category.SortOrder, category.IsActive, category.ParentId));
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

        var parentId = string.IsNullOrWhiteSpace(request.ParentId) ? null : request.ParentId.Trim();
        if (parentId is not null)
        {
            if (string.Equals(parentId, id, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "A category cannot be its own parent." });
            }

            if (!await db.Categories.AnyAsync(c => c.Id == parentId, cancellationToken))
            {
                return BadRequest(new { message = $"Parent category '{parentId}' does not exist." });
            }
        }

        var before = new { category.Label, category.SortOrder, category.IsActive, category.ParentId };
        category.Label = request.Label.Trim();
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;
        category.ParentId = parentId;

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
                after = new { category.Label, category.SortOrder, category.IsActive, category.ParentId },
            },
            ipAddress: HttpContext.GetClientIp(),
            requestPath: HttpContext.GetRequestPath(),
            cancellationToken: cancellationToken);

        return Ok(new CategoryDto(category.Id, category.Label, category.SortOrder, category.IsActive, category.ParentId));
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

        var inUse = await db.Products.AnyAsync(p => p.Category == id || p.SubCategoryId == id, cancellationToken);
        if (inUse)
        {
            return BadRequest(new { message = "Cannot delete a category that still has products." });
        }

        var hasChildren = await db.Categories.AnyAsync(c => c.ParentId == id, cancellationToken);
        if (hasChildren)
        {
            return BadRequest(new { message = "Cannot delete a category that still has subcategories." });
        }

        var snapshot = new { category.Id, category.Label, category.SortOrder, category.IsActive, category.ParentId };
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

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (page, pageSize);
    }
}
