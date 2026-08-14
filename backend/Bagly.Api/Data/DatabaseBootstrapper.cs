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
        await EnsureSellerPickupLocationsSchemaAsync(db, cancellationToken);
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

    /// <summary>
    /// Clears all business/user data tables while keeping <c>AdminUsers</c>, <c>Categories</c>,
    /// and <c>__EFMigrationsHistory</c>. Uses a single Postgres TRUNCATE so FK order is handled
    /// atomically. Safe to call repeatedly.
    /// </summary>
    public static async Task<Dictionary<string, int>> CleanupExceptAdminAndCategoriesAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BaglyDbContext>();

        Console.WriteLine("[setup/cleanup] Starting DB cleanup — keeping AdminUsers + Categories only.");

        // All tables in one TRUNCATE: Postgres disables FK checks among the listed tables for the
        // duration of the statement. Do NOT include AdminUsers, Categories, or __EFMigrationsHistory.
        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                "OrderShiprocketShipments",
                "OrderItems",
                "ShiprocketApiLogs",
                "SellerPickupLocations",
                "PaymentLogs",
                "ProductReviews",
                "StockAlerts",
                "CartItems",
                "ShippingAddresses",
                "Orders",
                "Carts",
                "Products",
                "CustomerUsers",
                "SellerUsers",
                "AuditLogs",
                "Logs",
                "ContactMessages",
                "SiteHits"
            RESTART IDENTITY;
            """,
            cancellationToken);

        var counts = new Dictionary<string, int>
        {
            ["AdminUsers"] = await db.AdminUsers.CountAsync(cancellationToken),
            ["Categories"] = await db.Categories.CountAsync(cancellationToken),
            ["Products"] = await db.Products.CountAsync(cancellationToken),
            ["Orders"] = await db.Orders.CountAsync(cancellationToken),
            ["OrderItems"] = await db.OrderItems.CountAsync(cancellationToken),
            ["OrderShiprocketShipments"] = await db.OrderShiprocketShipments.CountAsync(cancellationToken),
            ["Carts"] = await db.Carts.CountAsync(cancellationToken),
            ["CartItems"] = await db.CartItems.CountAsync(cancellationToken),
            ["CustomerUsers"] = await db.CustomerUsers.CountAsync(cancellationToken),
            ["SellerUsers"] = await db.SellerUsers.CountAsync(cancellationToken),
            ["SellerPickupLocations"] = await db.SellerPickupLocations.CountAsync(cancellationToken),
            ["ProductReviews"] = await db.ProductReviews.CountAsync(cancellationToken),
            ["ShippingAddresses"] = await db.ShippingAddresses.CountAsync(cancellationToken),
            ["StockAlerts"] = await db.StockAlerts.CountAsync(cancellationToken),
            ["PaymentLogs"] = await db.PaymentLogs.CountAsync(cancellationToken),
            ["ShiprocketApiLogs"] = await db.ShiprocketApiLogs.CountAsync(cancellationToken),
            ["AuditLogs"] = await db.AuditLogs.CountAsync(cancellationToken),
            ["Logs"] = await db.SystemLogs.CountAsync(cancellationToken),
            ["ContactMessages"] = await db.ContactMessages.CountAsync(cancellationToken),
            ["SiteHits"] = await db.SiteHits.CountAsync(cancellationToken),
        };

        Console.WriteLine(
            $"[setup/cleanup] Done. Kept AdminUsers={counts["AdminUsers"]}, Categories={counts["Categories"]}. " +
            $"Cleared Products={counts["Products"]}, Orders={counts["Orders"]}, SellerUsers={counts["SellerUsers"]}, " +
            $"CustomerUsers={counts["CustomerUsers"]}.");

        return counts;
    }

    /// <summary>Self-heal seller-owned Shiprocket pickup locations table.</summary>
    public static async Task EnsureSellerPickupLocationsSchemaAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "SellerPickupLocations" (
                    "Id" uuid NOT NULL,
                    "SellerUserId" uuid NOT NULL,
                    "PickupLocation" character varying(36) NOT NULL,
                    "Name" character varying(100) NOT NULL,
                    "Email" character varying(100) NOT NULL,
                    "Phone" character varying(15) NOT NULL,
                    "Address" character varying(80) NOT NULL,
                    "Address2" character varying(80),
                    "City" character varying(50) NOT NULL,
                    "State" character varying(50) NOT NULL,
                    "Country" character varying(50) NOT NULL,
                    "PinCode" character varying(12) NOT NULL,
                    "Lat" character varying(30),
                    "Long" character varying(30),
                    "Gstin" character varying(20),
                    "ShiprocketSuccess" boolean NOT NULL,
                    "ShiprocketPickupId" character varying(50),
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_SellerPickupLocations" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_SellerPickupLocations_SellerUsers_SellerUserId"
                        FOREIGN KEY ("SellerUserId") REFERENCES "SellerUsers" ("Id") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS "IX_SellerPickupLocations_SellerUserId"
                    ON "SellerPickupLocations" ("SellerUserId");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_SellerPickupLocations_SellerUserId_PickupLocation"
                    ON "SellerPickupLocations" ("SellerUserId", "PickupLocation");
                """,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Database bootstrap step 'SellerPickupLocationsSchemaSelfHeal' failed: {ex.Message}");
        }
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
                ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "UseDefaultPackageSize" boolean NOT NULL DEFAULT TRUE;
                ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "WeightKg" numeric(18,3);
                ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "LengthCm" numeric(18,2);
                ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "BreadthCm" numeric(18,2);
                ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "HeightCm" numeric(18,2);
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
