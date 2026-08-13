using System.Text.Json;
using System.Text.RegularExpressions;
using Bagly.Api.DTOs;
using Bagly.Api.Models;

namespace Bagly.Api.Mapping;

public static class ProductMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static ProductDto ToDto(Product product) =>
        new(
            product.Id,
            product.Name,
            product.Category,
            product.SubCategoryId,
            product.Price,
            product.CompareAt,
            DeserializeList(product.ColorsJson),
            product.Material,
            product.Rating,
            product.Reviews,
            product.Badge,
            product.ShortDescription,
            product.Description,
            DeserializeList(product.FeaturesJson),
            product.Image,
            DeserializeList(product.GalleryJson),
            product.StockQuantity,
            product.IsAvailable,
            product.StockQuantity > 0,
            product.Slug,
            product.SeoTitle,
            product.SeoDescription
        );

    public static AdminProductDto ToAdminDto(Product product) =>
        new(
            product.Id,
            product.Name,
            product.Category,
            product.SubCategoryId,
            product.Price,
            product.CompareAt,
            DeserializeList(product.ColorsJson),
            product.Material,
            product.Rating,
            product.Reviews,
            product.Badge,
            product.ShortDescription,
            product.Description,
            DeserializeList(product.FeaturesJson),
            product.Image,
            DeserializeList(product.GalleryJson),
            product.IsActive,
            product.StockQuantity,
            product.IsAvailable,
            product.CreatedAt,
            product.Slug,
            product.SeoTitle,
            product.SeoDescription,
            product.SeoKeywords,
            product.SellerId,
            product.ShiprocketPickupLocation
        );

    public static void ApplyUpsert(Product product, UpsertProductRequest request)
    {
        product.Name = request.Name.Trim();
        product.Category = request.Category.Trim();
        product.SubCategoryId = string.IsNullOrWhiteSpace(request.SubCategoryId) ? null : request.SubCategoryId.Trim();
        product.Price = request.Price;
        product.CompareAt = request.CompareAt;
        product.Material = request.Material?.Trim() ?? string.Empty;
        product.Rating = request.Rating;
        product.Reviews = request.Reviews;
        product.Badge = string.IsNullOrWhiteSpace(request.Badge) ? null : request.Badge.Trim();
        product.ShortDescription = request.ShortDescription?.Trim() ?? string.Empty;
        product.Description = request.Description?.Trim() ?? string.Empty;
        product.Image = request.Image?.Trim() ?? string.Empty;
        product.ColorsJson = JsonSerializer.Serialize(request.Colors ?? [], JsonOptions);
        product.FeaturesJson = JsonSerializer.Serialize(request.Features ?? [], JsonOptions);
        product.GalleryJson = JsonSerializer.Serialize(
            (request.Gallery?.Count > 0 ? request.Gallery : [request.Image ?? string.Empty])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList(),
            JsonOptions);
        product.IsActive = request.IsActive;
        product.StockQuantity = Math.Max(0, request.StockQuantity);
        product.SeoTitle = string.IsNullOrWhiteSpace(request.SeoTitle) ? null : request.SeoTitle.Trim();
        product.SeoDescription = string.IsNullOrWhiteSpace(request.SeoDescription) ? null : request.SeoDescription.Trim();
        product.SeoKeywords = string.IsNullOrWhiteSpace(request.SeoKeywords) ? null : request.SeoKeywords.Trim();
        product.ShiprocketPickupLocation = NormalizePickupNickname(request.ShiprocketPickupLocation);
    }

    /// <summary>Trim; empty → null. Does not rewrite case (Shiprocket nicknames are case-sensitive).</summary>
    public static string? NormalizePickupNickname(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > 100 ? trimmed[..100] : trimmed;
    }

    public static string Slugify(string value)
    {
        var slug = value.Trim().ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", string.Empty);
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = Regex.Replace(slug, @"-+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? $"product-{Guid.NewGuid():N}"[..16] : slug;
    }

    private static IReadOnlyList<string> DeserializeList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
