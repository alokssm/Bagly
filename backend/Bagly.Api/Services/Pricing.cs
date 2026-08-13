using Bagly.Api.DTOs;
using Bagly.Api.Models;

namespace Bagly.Api.Services;

public static class Pricing
{
    // Amounts are INR. Tiered shipping by cart subtotal:
    //   ₹0–₹499 → ₹79 | ₹500–₹999 → ₹49 | ₹1,000–₹2,499 → ₹29 | ₹2,500+ → free.
    // Empty cart / subtotal ≤ 0 → ₹0.
    public const decimal FreeShippingThreshold = 2500m;

    public static decimal CalculateShipping(decimal subtotal)
    {
        if (subtotal <= 0) return 0m;
        if (subtotal < 500m) return 79m;
        if (subtotal < 1000m) return 49m;
        if (subtotal < FreeShippingThreshold) return 29m;
        return 0m;
    }

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
