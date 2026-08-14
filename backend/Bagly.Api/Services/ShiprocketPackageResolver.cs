using Bagly.Api.Models;
using Bagly.Api.Options;

namespace Bagly.Api.Services;

/// <summary>
/// Resolves Shiprocket package weight/dimensions for a shipment group.
/// Per line: default-flagged products use <see cref="ShiprocketOptions"/> defaults;
/// custom products use product WeightKg + L/B/H (positive values; else fall back to options).
/// Shipment: <b>sum</b> (effectiveWeight × qty); <b>max</b> of each length/breadth/height axis.
/// </summary>
public static class ShiprocketPackageResolver
{
    public readonly record struct PackageSize(double WeightKg, double Length, double Breadth, double Height);

    public sealed record ProductPackageInfo(
        bool UseDefaultPackageSize,
        decimal? WeightKg,
        decimal? LengthCm,
        decimal? BreadthCm,
        decimal? HeightCm);

    public static ProductPackageInfo FromProduct(Product product) =>
        new(
            product.UseDefaultPackageSize,
            product.WeightKg,
            product.LengthCm,
            product.BreadthCm,
            product.HeightCm);

    public static PackageSize Defaults(ShiprocketOptions options)
    {
        var weight = options.DefaultWeightKg > 0 ? options.DefaultWeightKg : 0.5;
        var length = options.DefaultLength > 0 ? options.DefaultLength : 10;
        var breadth = options.DefaultBreadth > 0 ? options.DefaultBreadth : 15;
        var height = options.DefaultHeight > 0 ? options.DefaultHeight : 20;
        return new PackageSize(weight, length, breadth, height);
    }

    /// <summary>
    /// Aggregate package for order lines in one pickup/shipment group.
    /// Missing products are treated as default-package.
    /// </summary>
    public static PackageSize ResolveForLines(
        IEnumerable<(int Quantity, ProductPackageInfo? Package)> lines,
        ShiprocketOptions options)
    {
        var defaults = Defaults(options);
        double totalWeight = 0;
        double maxLength = 0;
        double maxBreadth = 0;
        double maxHeight = 0;
        var any = false;

        foreach (var (quantity, package) in lines)
        {
            any = true;
            var qty = quantity < 1 ? 1 : quantity;
            var (w, l, b, h) = EffectiveForProduct(package, defaults);
            totalWeight += w * qty;
            maxLength = Math.Max(maxLength, l);
            maxBreadth = Math.Max(maxBreadth, b);
            maxHeight = Math.Max(maxHeight, h);
        }

        if (!any)
        {
            return defaults;
        }

        return new PackageSize(
            totalWeight > 0 ? totalWeight : defaults.WeightKg,
            maxLength > 0 ? maxLength : defaults.Length,
            maxBreadth > 0 ? maxBreadth : defaults.Breadth,
            maxHeight > 0 ? maxHeight : defaults.Height);
    }

    private static (double Weight, double Length, double Breadth, double Height) EffectiveForProduct(
        ProductPackageInfo? package,
        PackageSize defaults)
    {
        if (package is null || package.UseDefaultPackageSize)
        {
            return (defaults.WeightKg, defaults.Length, defaults.Breadth, defaults.Height);
        }

        var w = package.WeightKg is > 0 ? (double)package.WeightKg.Value : defaults.WeightKg;
        var l = package.LengthCm is > 0 ? (double)package.LengthCm.Value : defaults.Length;
        var b = package.BreadthCm is > 0 ? (double)package.BreadthCm.Value : defaults.Breadth;
        var h = package.HeightCm is > 0 ? (double)package.HeightCm.Value : defaults.Height;
        return (w, l, b, h);
    }
}
