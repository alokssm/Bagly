using Bagly.Api.DTOs;
using Bagly.Api.Models;

namespace Bagly.Api.Services;

public static class Pricing
{
    public const decimal FreeShippingThreshold = 150m;
    public const decimal StandardShipping = 12m;

    public static decimal CalculateShipping(decimal subtotal) =>
        subtotal <= 0 || subtotal >= FreeShippingThreshold ? 0 : StandardShipping;

    public static CartDto ToCartDto(Cart cart)
    {
        var items = cart.Items
            .Select(i => new CartItemDto(i.ProductId, i.ProductName, i.Image, i.Color, i.UnitPrice, i.Quantity))
            .ToList();

        var subtotal = items.Sum(i => i.Price * i.Quantity);
        var shipping = CalculateShipping(subtotal);

        return new CartDto(
            cart.Id,
            items,
            items.Sum(i => i.Quantity),
            subtotal,
            shipping,
            subtotal + shipping
        );
    }
}
