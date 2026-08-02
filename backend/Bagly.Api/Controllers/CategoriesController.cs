using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController(BaglyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> GetCategories(CancellationToken cancellationToken)
    {
        // Public storefront only sees active categories (both top-level and subcategories),
        // e.g. inactive legacy categories stay hidden while School Bags remains the focus.
        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new CategoryDto(c.Id, c.Label, c.SortOrder, c.IsActive, c.ParentId))
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }
}
