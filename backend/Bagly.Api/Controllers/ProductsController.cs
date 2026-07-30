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
        [FromQuery] string? sort,
        CancellationToken cancellationToken)
    {
        var query = db.Products.AsNoTracking().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(category) &&
            !string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => p.Category == category);
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

    [HttpGet("{id}")]
    public async Task<ActionResult<ProductDto>> GetProduct(string id, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.IsActive, cancellationToken);

        if (product is null)
        {
            return NotFound(new { message = $"Product '{id}' was not found." });
        }

        return Ok(ProductMapper.ToDto(product));
    }
}
