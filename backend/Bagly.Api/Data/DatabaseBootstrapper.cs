using Bagly.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Data;

/// <summary>
/// Bootstraps the Postgres (Neon) database on startup. Neon databases are created empty, so the
/// happy path is simply "run pending EF migrations, then seed" — unlike the old Azure SQL setup,
/// there is no legacy schema drift to reconcile with hand-written T-SQL Ensure*Async helpers.
/// </summary>
public static class DatabaseBootstrapper
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BaglyDbContext>();
        var adminOptions = scope.ServiceProvider.GetRequiredService<IOptions<AdminOptions>>().Value;

        try
        {
            await db.Database.MigrateAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Log and continue so /api/health can still report the connection problem, and so a
            // partially-migrated database (e.g. a manual schema tweak on Neon) doesn't block the
            // API from starting entirely.
            Console.Error.WriteLine($"EF MigrateAsync warning: {ex.Message}");
        }

        await DbSeeder.SeedAsync(db, adminOptions);
    }

    /// <summary>Idempotent seed for empty Neon databases (safe to call multiple times).</summary>
    public static async Task<(int Categories, int Products, int Admins)> SeedOnlyAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BaglyDbContext>();
        var adminOptions = scope.ServiceProvider.GetRequiredService<IOptions<AdminOptions>>().Value;
        await DbSeeder.SeedAsync(db, adminOptions);
        return (
            await db.Categories.CountAsync(cancellationToken),
            await db.Products.CountAsync(cancellationToken),
            await db.AdminUsers.CountAsync(cancellationToken));
    }

    /// <summary>Re-applies the Products table's SubCategoryId + SEO columns using Postgres'
    /// <c>ADD COLUMN IF NOT EXISTS</c>, which is safe to call repeatedly/concurrently. Used as a
    /// self-heal fallback by ProductsController when a query fails with Postgres error 42703
    /// (undefined_column) — e.g. the app started before an EF migration finished applying, or a
    /// column was manually dropped on Neon.</summary>
    public static async Task EnsureProductsSchemaAsync(BaglyDbContext db, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "SubCategoryId" character varying(50);
                ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "Slug" character varying(160);
                ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "SeoTitle" character varying(160);
                ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "SeoDescription" character varying(300);
                ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "SeoKeywords" character varying(300);
                ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "SellerId" uuid;
                UPDATE "Products" SET "Slug" = "Id" WHERE "Slug" IS NULL OR "Slug" = '';
                """,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Database bootstrap step 'ProductsSchemaSelfHeal' failed: {ex.Message}");
        }
    }
}
