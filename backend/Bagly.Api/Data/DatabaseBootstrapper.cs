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

        await EnsureOrdersShiprocketSchemaAsync(db, cancellationToken);
        await EnsureProductsSchemaAsync(db, cancellationToken);
        await EnsureOrderShiprocketShipmentsSchemaAsync(db, cancellationToken);
        await EnsureOrderShiprocketShippingFieldsAsync(db, cancellationToken);
        await EnsureShiprocketApiLogsSchemaAsync(db, cancellationToken);
        await DbSeeder.SeedAsync(db, adminOptions);
    }

    /// <summary>
    /// Self-heal Orders Shiprocket/Phone columns if MigrateAsync was skipped or partially applied.
    /// Safe to call repeatedly (ADD COLUMN IF NOT EXISTS).
    /// </summary>
    public static async Task EnsureOrdersShiprocketSchemaAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "Phone" character varying(30);
                ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "ShiprocketOrderId" character varying(50);
                ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "ShiprocketShipmentId" character varying(50);
                ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "ShiprocketStatus" character varying(50);
                ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "ShiprocketLastError" character varying(500);
                """,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Database bootstrap step 'OrdersShiprocketSchemaSelfHeal' failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Self-heal multi-pickup shipment table if MigrateAsync was skipped or partially applied.
    /// </summary>
    public static async Task EnsureOrderShiprocketShipmentsSchemaAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "OrderShiprocketShipments" (
                    "Id" uuid NOT NULL,
                    "OrderId" uuid NOT NULL,
                    "PickupLocation" character varying(100) NOT NULL,
                    "ShiprocketOrderId" character varying(50),
                    "ShiprocketShipmentId" character varying(50),
                    "Status" character varying(50),
                    "LastError" character varying(500),
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone,
                    CONSTRAINT "PK_OrderShiprocketShipments" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_OrderShiprocketShipments_Orders_OrderId"
                        FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS "IX_OrderShiprocketShipments_OrderId"
                    ON "OrderShiprocketShipments" ("OrderId");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_OrderShiprocketShipments_OrderId_PickupLocation"
                    ON "OrderShiprocketShipments" ("OrderId", "PickupLocation");
                CREATE INDEX IF NOT EXISTS "IX_OrderShiprocketShipments_ShiprocketOrderId"
                    ON "OrderShiprocketShipments" ("ShiprocketOrderId");
                """,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Database bootstrap step 'OrderShiprocketShipmentsSchemaSelfHeal' failed: {ex.Message}");
        }
    }

    /// <summary>Self-heal AWB / Ready-to-Ship columns on OrderShiprocketShipments.</summary>
    public static async Task EnsureOrderShiprocketShippingFieldsAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "OrderShiprocketShipments" ADD COLUMN IF NOT EXISTS "ShippingStatus" character varying(50);
                ALTER TABLE "OrderShiprocketShipments" ADD COLUMN IF NOT EXISTS "AwbCode" character varying(50);
                ALTER TABLE "OrderShiprocketShipments" ADD COLUMN IF NOT EXISTS "CourierId" integer;
                ALTER TABLE "OrderShiprocketShipments" ADD COLUMN IF NOT EXISTS "CourierName" character varying(100);
                ALTER TABLE "OrderShiprocketShipments" ADD COLUMN IF NOT EXISTS "ActualShippingCharge" numeric(18,2);
                ALTER TABLE "OrderShiprocketShipments" ADD COLUMN IF NOT EXISTS "ReadyToShipAt" timestamp with time zone;
                ALTER TABLE "OrderShiprocketShipments" ADD COLUMN IF NOT EXISTS "AwbAssignedAt" timestamp with time zone;
                CREATE INDEX IF NOT EXISTS "IX_OrderShiprocketShipments_AwbCode"
                    ON "OrderShiprocketShipments" ("AwbCode");
                CREATE INDEX IF NOT EXISTS "IX_OrderShiprocketShipments_ShippingStatus"
                    ON "OrderShiprocketShipments" ("ShippingStatus");
                """,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Database bootstrap step 'OrderShiprocketShippingFieldsSelfHeal' failed: {ex.Message}");
        }
    }

    /// <summary>Self-heal Shiprocket API request log table.</summary>
    public static async Task EnsureShiprocketApiLogsSchemaAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "ShiprocketApiLogs" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                    "OrderId" uuid,
                    "ShipmentId" uuid,
                    "Action" character varying(80) NOT NULL,
                    "HttpMethod" character varying(10) NOT NULL,
                    "Url" character varying(500) NOT NULL,
                    "RequestJson" text,
                    "ResponseStatus" integer,
                    "ResponseJson" text,
                    "CreatedAtUtc" timestamp with time zone NOT NULL,
                    "AdminEmail" character varying(256),
                    CONSTRAINT "PK_ShiprocketApiLogs" PRIMARY KEY ("Id")
                );
                CREATE INDEX IF NOT EXISTS "IX_ShiprocketApiLogs_CreatedAtUtc"
                    ON "ShiprocketApiLogs" ("CreatedAtUtc");
                CREATE INDEX IF NOT EXISTS "IX_ShiprocketApiLogs_OrderId"
                    ON "ShiprocketApiLogs" ("OrderId");
                CREATE INDEX IF NOT EXISTS "IX_ShiprocketApiLogs_ShipmentId"
                    ON "ShiprocketApiLogs" ("ShipmentId");
                CREATE INDEX IF NOT EXISTS "IX_ShiprocketApiLogs_Action"
                    ON "ShiprocketApiLogs" ("Action");
                """,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Database bootstrap step 'ShiprocketApiLogsSchemaSelfHeal' failed: {ex.Message}");
        }
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
                ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "ShiprocketPickupLocation" character varying(100);
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
