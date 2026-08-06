using System.ComponentModel.DataAnnotations.Schema;

namespace Bagly.Api.Models;

public class Product
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    /// <summary>Optional subcategory id (Category.Id of a category whose ParentId == Category), e.g. "boys".</summary>
    public string? SubCategoryId { get; set; }
    public decimal Price { get; set; }
    public decimal? CompareAt { get; set; }
    public string Material { get; set; } = string.Empty;
    public double Rating { get; set; }
    public int Reviews { get; set; }
    public string? Badge { get; set; }
    public string ShortDescription { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string ColorsJson { get; set; } = "[]";
    public string FeaturesJson { get; set; } = "[]";
    public string GalleryJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public int StockQuantity { get; set; } = 999;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>URL-friendly, unique-ish identifier used for SEO-friendly product links
    /// (e.g. <c>/product/leather-tote-bag</c>). Backfilled from <see cref="Id"/> for legacy rows.</summary>
    public string? Slug { get; set; }

    /// <summary>Optional override for the storefront <c>&lt;title&gt;</c> tag; falls back to <see cref="Name"/> when empty.</summary>
    public string? SeoTitle { get; set; }

    /// <summary>Optional override for the storefront meta description tag; falls back to <see cref="ShortDescription"/> when empty.</summary>
    public string? SeoDescription { get; set; }

    /// <summary>Optional comma-separated focus keywords for search engines.</summary>
    public string? SeoKeywords { get; set; }

    /// <summary>
    /// Owning marketplace seller. Null = platform/legacy catalog (seeded Bagly products).
    /// Seller-created products set this to the logged-in seller's Id.
    /// </summary>
    public Guid? SellerId { get; set; }

    public SellerUser? Seller { get; set; }

    /// <summary>A product can be bought when it is active and in stock.</summary>
    [NotMapped]
    public bool IsAvailable => IsActive && StockQuantity > 0;
}
