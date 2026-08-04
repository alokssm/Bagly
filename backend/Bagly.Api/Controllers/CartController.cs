using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Models;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CartController(BaglyDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CartDto>> CreateCart(CancellationToken cancellationToken)
    {
        var cart = new Cart();
        db.Carts.Add(cart);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(Pricing.ToCartDto(cart));
    }

    [HttpGet("{cartId:guid}")]
    public async Task<ActionResult<CartDto>> GetCart(Guid cartId, CancellationToken cancellationToken)
    {
        var cart = await LoadCartAsync(cartId, cancellationToken);
        return cart is null ? NotFound(new { message = "Cart not found." }) : Ok(Pricing.ToCartDto(cart));
    }

    [HttpPost("{cartId:guid}/items")]
    public async Task<ActionResult<CartDto>> AddItem(
        Guid cartId,
        [FromBody] AddCartItemRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Quantity < 1)
        {
            return BadRequest(new { message = "Quantity must be at least 1." });
        }

        // The frontend generates the cart id client-side so the very first add-to-cart is a
        // single request (no separate create-cart round trip first) — create the row here on
        // demand instead of requiring it to already exist.
        var cart = await LoadCartAsync(cartId, cancellationToken);
        if (cart is null)
        {
            cart = new Cart { Id = cartId };
            db.Carts.Add(cart);
        }

        // Project only what's needed for the cart line instead of loading the full product
        // row (descriptions, SEO fields, gallery JSON, etc.) that AddItem never uses.
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == request.ProductId && p.IsActive)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Image,
                p.Price,
                p.StockQuantity,
                p.ColorsJson,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return NotFound(new { message = "Product not found." });
        }

        if (product.StockQuantity <= 0)
        {
            return BadRequest(new { message = $"'{product.Name}' is sold out and cannot be added to cart." });
        }

        var colors = System.Text.Json.JsonSerializer.Deserialize<List<string>>(product.ColorsJson) ?? [];
        var color = string.IsNullOrWhiteSpace(request.Color) ? colors.FirstOrDefault() ?? "Default" : request.Color;

        var existing = cart.Items.FirstOrDefault(i => i.ProductId == product.Id && i.Color == color);
        var requestedTotal = (existing?.Quantity ?? 0) + request.Quantity;
        if (requestedTotal > product.StockQuantity)
        {
            return BadRequest(new
            {
                message = $"Only {product.StockQuantity} left in stock for '{product.Name}'.",
            });
        }

        if (existing is not null)
        {
            existing.Quantity += request.Quantity;
        }
        else
        {
            cart.Items.Add(new CartItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Image = product.Image,
                Color = color,
                UnitPrice = product.Price,
                Quantity = request.Quantity,
            });
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(Pricing.ToCartDto(cart));
    }

    [HttpPut("{cartId:guid}/items/{productId}")]
    public async Task<ActionResult<CartDto>> UpdateItem(
        Guid cartId,
        string productId,
        [FromQuery] string color,
        [FromBody] UpdateCartItemRequest request,
        CancellationToken cancellationToken)
    {
        var cart = await LoadCartAsync(cartId, cancellationToken);
        if (cart is null)
        {
            return NotFound(new { message = "Cart not found." });
        }

        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId && i.Color == color);
        if (item is null)
        {
            return NotFound(new { message = "Cart item not found." });
        }

        if (request.Quantity <= 0)
        {
            db.CartItems.Remove(item);
        }
        else
        {
            var product = await db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

            if (product is not null && request.Quantity > product.StockQuantity)
            {
                return BadRequest(new
                {
                    message = product.StockQuantity <= 0
                        ? $"'{product.Name}' is sold out."
                        : $"Only {product.StockQuantity} left in stock for '{product.Name}'.",
                });
            }

            item.Quantity = request.Quantity;
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(Pricing.ToCartDto(cart));
    }

    [HttpDelete("{cartId:guid}/items/{productId}")]
    public async Task<ActionResult<CartDto>> RemoveItem(
        Guid cartId,
        string productId,
        [FromQuery] string color,
        CancellationToken cancellationToken)
    {
        var cart = await LoadCartAsync(cartId, cancellationToken);
        if (cart is null)
        {
            return NotFound(new { message = "Cart not found." });
        }

        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId && i.Color == color);
        if (item is null)
        {
            return NotFound(new { message = "Cart item not found." });
        }

        db.CartItems.Remove(item);
        cart.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(Pricing.ToCartDto(cart));
    }

    [HttpDelete("{cartId:guid}")]
    public async Task<ActionResult<CartDto>> ClearCart(Guid cartId, CancellationToken cancellationToken)
    {
        var cart = await LoadCartAsync(cartId, cancellationToken);
        if (cart is null)
        {
            return NotFound(new { message = "Cart not found." });
        }

        db.CartItems.RemoveRange(cart.Items);
        cart.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(Pricing.ToCartDto(cart));
    }

    private Task<Cart?> LoadCartAsync(Guid cartId, CancellationToken cancellationToken) =>
        db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == cartId, cancellationToken);
}
