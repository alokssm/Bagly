namespace Bagly.Api.Models;

public class Category
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Null for a top-level category. When set, this category is a subcategory nested
    /// under the category whose Id matches this value (e.g. "boys" under "school-bags").
    /// </summary>
    public string? ParentId { get; set; }
}
