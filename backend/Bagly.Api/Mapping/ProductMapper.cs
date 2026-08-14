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
            product.ShiprocketPickupLocation,
            product.UseDefaultPackageSize,
            product.WeightKg,
            product.LengthCm,
            product.BreadthCm,
            product.HeightCm
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
        ApplyPackageFields(
            product,
            request.UseDefaultPackageSize,
            request.WeightKg,
            request.LengthCm,
            request.BreadthCm,
            request.HeightCm);
    }

    public static void ApplyPackageFields(
        Product product,
        bool useDefaultPackageSize,
        decimal? weightKg,
        decimal? lengthCm,
        decimal? breadthCm,
        decimal? heightCm)
    {
        product.UseDefaultPackageSize = useDefaultPackageSize;
        if (useDefaultPackageSize)
        {
            // Preserve previously entered custom values when toggling back to defaults,
            // but still accept positive values if the client sent them.
            if (weightKg is > 0) product.WeightKg = weightKg;
            if (lengthCm is > 0) product.LengthCm = lengthCm;
            if (breadthCm is > 0) product.BreadthCm = breadthCm;
            if (heightCm is > 0) product.HeightCm = heightCm;
            return;
        }

        product.WeightKg = weightKg;
        product.LengthCm = lengthCm;
        product.BreadthCm = breadthCm;
        product.HeightCm = heightCm;
    }

    /// <summary>
    /// When <paramref name="useDefaultPackageSize"/> is false, weight and L/B/H must be present and &gt; 0.
    /// </summary>
    public static string? ValidatePackageFields(
        bool useDefaultPackageSize,
        decimal? weightKg,
        decimal? lengthCm,
        decimal? breadthCm,
        decimal? heightCm)
    {
        if (useDefaultPackageSize)
        {
            return null;
        }

        if (weightKg is null or <= 0) return "Weight (kg) is required when not using the default package size.";
        if (lengthCm is null or <= 0) return "Length (cm) is required when not using the default package size.";
        if (breadthCm is null or <= 0) return "Breadth (cm) is required when not using the default package size.";
        if (heightCm is null or <= 0) return "Height (cm) is required when not using the default package size.";
        return null;
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
