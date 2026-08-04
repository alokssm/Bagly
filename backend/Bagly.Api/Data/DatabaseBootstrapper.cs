using Bagly.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Data;

public static class DatabaseBootstrapper
{
    private const string InitialMigrationId = "20260729121148_InitialCreate";

    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BaglyDbContext>();
        var adminOptions = scope.ServiceProvider.GetRequiredService<IOptions<AdminOptions>>().Value;

        await EnsureMigrationsHistoryTableAsync(db, cancellationToken);

        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToHashSet();
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        // Legacy: mark InitialCreate applied when base schema already exists from SQL scripts.
        if (pending.Contains(InitialMigrationId) &&
            !applied.Contains(InitialMigrationId) &&
            await TableExistsAsync(db, "Products", cancellationToken))
        {
            await MarkMigrationAppliedAsync(db, InitialMigrationId, cancellationToken);
        }

        // Ensure AdminUsers exists even if migration has not run yet.
        await EnsureAdminUsersTableAsync(db, cancellationToken);
        await EnsureCustomerUsersTableAsync(db, cancellationToken);
        await EnsureAuditLogsTableAsync(db, cancellationToken);
        await EnsurePaymentLogsTableAsync(db, cancellationToken);
        await EnsureOrderPaymentColumnsAsync(db, cancellationToken);
        await EnsureStockQuantityAndAlertsAsync(db, cancellationToken);
        await EnsureShippingAddressesTableAsync(db, cancellationToken);
        await EnsureOrderCustomerUserIdColumnAsync(db, cancellationToken);
        await EnsureCategoryHierarchyAndSubCategoryColumnsAsync(db, cancellationToken);
        await EnsureContactMessagesTableAsync(db, cancellationToken);
        await EnsureSiteHitsTableAsync(db, cancellationToken);

        // Re-read pending after possible history updates.
        pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        foreach (var migrationId in pending)
        {
            var shouldMark =
                (migrationId.Contains("AdminUsers", StringComparison.OrdinalIgnoreCase) &&
                 await TableExistsAsync(db, "AdminUsers", cancellationToken)) ||
                (migrationId.Contains("AuditLogs", StringComparison.OrdinalIgnoreCase) &&
                 await TableExistsAsync(db, "AuditLogs", cancellationToken)) ||
                (migrationId.Contains("PaymentLogs", StringComparison.OrdinalIgnoreCase) &&
                 await TableExistsAsync(db, "PaymentLogs", cancellationToken) &&
                 await ColumnExistsAsync(db, "Orders", "PaymentStatus", cancellationToken)) ||
                (migrationId.Contains("CustomerUserId", StringComparison.OrdinalIgnoreCase) &&
                 await ColumnExistsAsync(db, "Orders", "CustomerUserId", cancellationToken)) ||
                (migrationId.Contains("SchoolBagsCategoryHierarchy", StringComparison.OrdinalIgnoreCase) &&
                 await ColumnExistsAsync(db, "Categories", "ParentId", cancellationToken) &&
                 await ColumnExistsAsync(db, "Products", "SubCategoryId", cancellationToken)) ||
                (migrationId.Contains("ContactMessages", StringComparison.OrdinalIgnoreCase) &&
                 await TableExistsAsync(db, "ContactMessages", cancellationToken)) ||
                (migrationId.Contains("SiteHits", StringComparison.OrdinalIgnoreCase) &&
                 await TableExistsAsync(db, "SiteHits", cancellationToken));

            if (shouldMark)
            {
                await MarkMigrationAppliedAsync(db, migrationId, cancellationToken);
            }
        }

        pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            try
            {
                await db.Database.MigrateAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Azure may already have schema from Ensure* helpers; continue to seed.
                Console.Error.WriteLine($"EF MigrateAsync warning: {ex.Message}");
            }
        }

        await DbSeeder.SeedAsync(db, adminOptions);
    }

    /// <summary>Idempotent seed for empty Azure databases (safe to call multiple times).</summary>
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

    private static async Task EnsureMigrationsHistoryTableAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[__EFMigrationsHistory]
                (
                    [MigrationId]    NVARCHAR(150) NOT NULL,
                    [ProductVersion] NVARCHAR(32)  NOT NULL,
                    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                );
            END
            """,
            cancellationToken);
    }

    private static Task MarkMigrationAppliedAsync(
        BaglyDbContext db,
        string migrationId,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $@"IF NOT EXISTS (
                    SELECT 1 FROM [dbo].[__EFMigrationsHistory]
                    WHERE [MigrationId] = {migrationId})
               INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
               VALUES ({migrationId}, {"8.0.11"});",
            cancellationToken);

    private static async Task EnsureAdminUsersTableAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(db, "AdminUsers", cancellationToken))
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [dbo].[AdminUsers]
            (
                [Id]           UNIQUEIDENTIFIER NOT NULL,
                [Email]        NVARCHAR(256)    NOT NULL,
                [Name]         NVARCHAR(150)    NOT NULL,
                [PasswordHash] NVARCHAR(500)    NOT NULL,
                [Role]         NVARCHAR(50)     NOT NULL,
                [IsActive]     BIT              NOT NULL CONSTRAINT [DF_AdminUsers_IsActive] DEFAULT (1),
                [CreatedAt]    DATETIME2        NOT NULL,
                [LastLoginAt]  DATETIME2        NULL,
                CONSTRAINT [PK_AdminUsers] PRIMARY KEY ([Id])
            );

            CREATE UNIQUE INDEX [IX_AdminUsers_Email] ON [dbo].[AdminUsers] ([Email]);
            CREATE INDEX [IX_AdminUsers_IsActive] ON [dbo].[AdminUsers] ([IsActive]);
            """,
            cancellationToken);
    }

    private static async Task EnsureCustomerUsersTableAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(db, "CustomerUsers", cancellationToken))
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [dbo].[CustomerUsers]
            (
                [Id]            UNIQUEIDENTIFIER NOT NULL,
                [Email]         NVARCHAR(256)    NOT NULL,
                [Name]          NVARCHAR(150)    NOT NULL,
                [PasswordHash]  NVARCHAR(500)    NULL,
                [GoogleSubject] NVARCHAR(100)    NULL,
                [IsActive]      BIT              NOT NULL CONSTRAINT [DF_CustomerUsers_IsActive] DEFAULT (1),
                [CreatedAt]     DATETIME2        NOT NULL,
                [LastLoginAt]   DATETIME2        NULL,
                CONSTRAINT [PK_CustomerUsers] PRIMARY KEY ([Id])
            );

            CREATE UNIQUE INDEX [IX_CustomerUsers_Email] ON [dbo].[CustomerUsers] ([Email]);
            CREATE UNIQUE INDEX [IX_CustomerUsers_GoogleSubject] ON [dbo].[CustomerUsers] ([GoogleSubject]) WHERE [GoogleSubject] IS NOT NULL;
            CREATE INDEX [IX_CustomerUsers_IsActive] ON [dbo].[CustomerUsers] ([IsActive]);
            """,
            cancellationToken);
    }

    private static async Task EnsureAuditLogsTableAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(db, "AuditLogs", cancellationToken))
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [dbo].[AuditLogs]
            (
                [Id]           BIGINT IDENTITY(1,1) NOT NULL,
                [TimestampUtc] DATETIME2            NOT NULL,
                [Level]        NVARCHAR(32)         NOT NULL,
                [Category]     NVARCHAR(50)         NOT NULL,
                [Action]       NVARCHAR(100)        NOT NULL,
                [ActorEmail]   NVARCHAR(256)        NULL,
                [EntityType]   NVARCHAR(100)        NULL,
                [EntityId]     NVARCHAR(100)        NULL,
                [Message]      NVARCHAR(2000)       NOT NULL,
                [DetailsJson]  NVARCHAR(MAX)        NULL,
                [IpAddress]    NVARCHAR(64)         NULL,
                [RequestPath]  NVARCHAR(500)        NULL,
                CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
            );

            CREATE INDEX [IX_AuditLogs_TimestampUtc] ON [dbo].[AuditLogs] ([TimestampUtc]);
            CREATE INDEX [IX_AuditLogs_Category] ON [dbo].[AuditLogs] ([Category]);
            CREATE INDEX [IX_AuditLogs_Action] ON [dbo].[AuditLogs] ([Action]);
            CREATE INDEX [IX_AuditLogs_ActorEmail] ON [dbo].[AuditLogs] ([ActorEmail]);
            """,
            cancellationToken);
    }

    private static async Task EnsurePaymentLogsTableAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(db, "PaymentLogs", cancellationToken))
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [dbo].[PaymentLogs]
            (
                [Id]                 BIGINT IDENTITY(1,1) NOT NULL,
                [TimestampUtc]       DATETIME2            NOT NULL,
                [OrderId]            UNIQUEIDENTIFIER     NULL,
                [OrderNumber]        NVARCHAR(50)         NULL,
                [Provider]           NVARCHAR(50)         NOT NULL,
                [EventType]          NVARCHAR(50)         NOT NULL,
                [Status]             NVARCHAR(50)         NOT NULL,
                [RazorpayOrderId]    NVARCHAR(100)        NULL,
                [RazorpayPaymentId]  NVARCHAR(100)        NULL,
                [RazorpaySignature]  NVARCHAR(256)        NULL,
                [Amount]             DECIMAL(18,2)        NULL,
                [Currency]           NVARCHAR(10)         NULL,
                [CustomerEmail]      NVARCHAR(256)        NULL,
                [Message]            NVARCHAR(2000)       NOT NULL,
                [RequestJson]        NVARCHAR(MAX)        NULL,
                [ResponseJson]       NVARCHAR(MAX)        NULL,
                [ErrorCode]          NVARCHAR(100)        NULL,
                [IpAddress]          NVARCHAR(64)         NULL,
                CONSTRAINT [PK_PaymentLogs] PRIMARY KEY ([Id])
            );

            CREATE INDEX [IX_PaymentLogs_TimestampUtc] ON [dbo].[PaymentLogs] ([TimestampUtc]);
            CREATE INDEX [IX_PaymentLogs_OrderId] ON [dbo].[PaymentLogs] ([OrderId]);
            CREATE INDEX [IX_PaymentLogs_RazorpayOrderId] ON [dbo].[PaymentLogs] ([RazorpayOrderId]);
            CREATE INDEX [IX_PaymentLogs_EventType] ON [dbo].[PaymentLogs] ([EventType]);
            CREATE INDEX [IX_PaymentLogs_Status] ON [dbo].[PaymentLogs] ([Status]);
            """,
            cancellationToken);
    }

    private static async Task EnsureOrderPaymentColumnsAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(db, "Orders", cancellationToken))
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            IF COL_LENGTH('dbo.Orders', 'PaymentStatus') IS NULL
                ALTER TABLE [dbo].[Orders] ADD [PaymentStatus] NVARCHAR(50) NOT NULL CONSTRAINT [DF_Orders_PaymentStatus] DEFAULT (N'NotRequired');

            IF COL_LENGTH('dbo.Orders', 'PaymentProvider') IS NULL
                ALTER TABLE [dbo].[Orders] ADD [PaymentProvider] NVARCHAR(50) NULL;

            IF COL_LENGTH('dbo.Orders', 'Currency') IS NULL
                ALTER TABLE [dbo].[Orders] ADD [Currency] NVARCHAR(10) NULL;

            IF COL_LENGTH('dbo.Orders', 'AmountInr') IS NULL
                ALTER TABLE [dbo].[Orders] ADD [AmountInr] DECIMAL(18,2) NULL;

            IF COL_LENGTH('dbo.Orders', 'RazorpayOrderId') IS NULL
                ALTER TABLE [dbo].[Orders] ADD [RazorpayOrderId] NVARCHAR(100) NULL;

            IF COL_LENGTH('dbo.Orders', 'RazorpayPaymentId') IS NULL
                ALTER TABLE [dbo].[Orders] ADD [RazorpayPaymentId] NVARCHAR(100) NULL;

            IF COL_LENGTH('dbo.Orders', 'PaidAtUtc') IS NULL
                ALTER TABLE [dbo].[Orders] ADD [PaidAtUtc] DATETIME2 NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_RazorpayOrderId' AND object_id = OBJECT_ID(N'dbo.Orders'))
                CREATE INDEX [IX_Orders_RazorpayOrderId] ON [dbo].[Orders] ([RazorpayOrderId]);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_PaymentStatus' AND object_id = OBJECT_ID(N'dbo.Orders'))
                CREATE INDEX [IX_Orders_PaymentStatus] ON [dbo].[Orders] ([PaymentStatus]);
            """,
            cancellationToken);
    }

    private static async Task EnsureStockQuantityAndAlertsAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(db, "Products", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH('dbo.Products', 'StockQuantity') IS NULL
                    ALTER TABLE [dbo].[Products] ADD [StockQuantity] INT NOT NULL CONSTRAINT [DF_Products_StockQuantity] DEFAULT (999);
                """,
                cancellationToken);
        }

        if (await TableExistsAsync(db, "StockAlerts", cancellationToken))
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [dbo].[StockAlerts]
            (
                [Id]         INT IDENTITY(1,1) NOT NULL,
                [ProductId]  NVARCHAR(100)     NOT NULL,
                [Email]      NVARCHAR(256)     NOT NULL,
                [Notified]   BIT               NOT NULL CONSTRAINT [DF_StockAlerts_Notified] DEFAULT (0),
                [CreatedAt]  DATETIME2         NOT NULL,
                [NotifiedAt] DATETIME2         NULL,
                CONSTRAINT [PK_StockAlerts] PRIMARY KEY ([Id])
            );

            CREATE UNIQUE INDEX [IX_StockAlerts_Email_ProductId] ON [dbo].[StockAlerts] ([Email], [ProductId]);
            CREATE INDEX [IX_StockAlerts_ProductId] ON [dbo].[StockAlerts] ([ProductId]);
            CREATE INDEX [IX_StockAlerts_Notified] ON [dbo].[StockAlerts] ([Notified]);
            """,
            cancellationToken);
    }

    private static async Task EnsureShippingAddressesTableAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(db, "ShippingAddresses", cancellationToken))
        {
            return;
        }

        var fkClause = await TableExistsAsync(db, "CustomerUsers", cancellationToken)
            ? """
              ,
                  CONSTRAINT [FK_ShippingAddresses_CustomerUsers] FOREIGN KEY ([CustomerUserId])
                      REFERENCES [dbo].[CustomerUsers] ([Id]) ON DELETE CASCADE
              """
            : string.Empty;

        var createTableSql = string.Concat(
            """
            CREATE TABLE [dbo].[ShippingAddresses]
            (
                [Id]              UNIQUEIDENTIFIER NOT NULL,
                [CustomerUserId]  UNIQUEIDENTIFIER NOT NULL,
                [Label]           NVARCHAR(50)     NULL,
                [FirstName]       NVARCHAR(100)    NOT NULL,
                [LastName]        NVARCHAR(100)    NOT NULL,
                [Email]           NVARCHAR(256)    NOT NULL,
                [Phone]           NVARCHAR(30)     NULL,
                [Address]         NVARCHAR(300)    NOT NULL,
                [City]            NVARCHAR(100)    NOT NULL,
                [State]           NVARCHAR(100)    NOT NULL,
                [Zip]             NVARCHAR(20)     NOT NULL,
                [Country]         NVARCHAR(100)    NOT NULL,
                [IsDefault]       BIT              NOT NULL CONSTRAINT [DF_ShippingAddresses_IsDefault] DEFAULT (0),
                [CreatedAt]       DATETIME2        NOT NULL,
                CONSTRAINT [PK_ShippingAddresses] PRIMARY KEY ([Id])
            """,
            fkClause,
            """

            );

            CREATE INDEX [IX_ShippingAddresses_CustomerUserId] ON [dbo].[ShippingAddresses] ([CustomerUserId]);
            CREATE INDEX [IX_ShippingAddresses_CustomerUserId_IsDefault] ON [dbo].[ShippingAddresses] ([CustomerUserId], [IsDefault]);
            """);

        await db.Database.ExecuteSqlRawAsync(createTableSql, cancellationToken);
    }

    /// <summary>
    /// Adds Orders.CustomerUserId so checkout orders can be linked to the placing customer's
    /// account directly, instead of relying solely on a case-insensitive email match (which
    /// breaks when the shipping email at checkout differs from the account email).
    /// </summary>
    private static async Task EnsureOrderCustomerUserIdColumnAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(db, "Orders", cancellationToken))
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            IF COL_LENGTH('dbo.Orders', 'CustomerUserId') IS NULL
                ALTER TABLE [dbo].[Orders] ADD [CustomerUserId] UNIQUEIDENTIFIER NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_CustomerUserId' AND object_id = OBJECT_ID(N'dbo.Orders'))
                CREATE INDEX [IX_Orders_CustomerUserId] ON [dbo].[Orders] ([CustomerUserId]);
            """,
            cancellationToken);
    }

    /// <summary>
    /// Adds Categories.IsActive + Categories.ParentId (subcategory hierarchy, e.g. School Bags →
    /// Boys/Girls/Kids) and Products.SubCategoryId so existing Azure/Render databases pick up the
    /// School Bags catalog without a full migration.
    /// </summary>
    private static async Task EnsureCategoryHierarchyAndSubCategoryColumnsAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(db, "Categories", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH('dbo.Categories', 'IsActive') IS NULL
                    ALTER TABLE [dbo].[Categories] ADD [IsActive] BIT NOT NULL CONSTRAINT [DF_Categories_IsActive] DEFAULT (1);

                IF COL_LENGTH('dbo.Categories', 'ParentId') IS NULL
                    ALTER TABLE [dbo].[Categories] ADD [ParentId] NVARCHAR(50) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Categories_ParentId' AND object_id = OBJECT_ID(N'dbo.Categories'))
                    CREATE INDEX [IX_Categories_ParentId] ON [dbo].[Categories] ([ParentId]);
                """,
                cancellationToken);
        }

        if (await TableExistsAsync(db, "Products", cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH('dbo.Products', 'SubCategoryId') IS NULL
                    ALTER TABLE [dbo].[Products] ADD [SubCategoryId] NVARCHAR(50) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Products_SubCategoryId' AND object_id = OBJECT_ID(N'dbo.Products'))
                    CREATE INDEX [IX_Products_SubCategoryId] ON [dbo].[Products] ([SubCategoryId]);
                """,
                cancellationToken);
        }
    }

    /// <summary>Adds the ContactMessages table used by the public "Contact us" form so already
    /// deployed Azure/Render databases pick it up without a full migration.</summary>
    private static async Task EnsureContactMessagesTableAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(db, "ContactMessages", cancellationToken))
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [dbo].[ContactMessages]
            (
                [Id]          INT IDENTITY(1,1) NOT NULL,
                [FirstName]   NVARCHAR(100)     NOT NULL,
                [LastName]    NVARCHAR(100)     NOT NULL,
                [Phone]       NVARCHAR(30)      NOT NULL,
                [Email]       NVARCHAR(256)     NOT NULL,
                [CompanyName] NVARCHAR(200)     NULL,
                [Message]     NVARCHAR(4000)    NOT NULL,
                [IpAddress]   NVARCHAR(64)      NULL,
                [EmailSent]   BIT               NOT NULL CONSTRAINT [DF_ContactMessages_EmailSent] DEFAULT (0),
                [CreatedAt]   DATETIME2         NOT NULL,
                CONSTRAINT [PK_ContactMessages] PRIMARY KEY ([Id])
            );

            CREATE INDEX [IX_ContactMessages_CreatedAt] ON [dbo].[ContactMessages] ([CreatedAt]);
            CREATE INDEX [IX_ContactMessages_Email] ON [dbo].[ContactMessages] ([Email]);
            """,
            cancellationToken);
    }

    /// <summary>Adds the SiteHits table used by the public storefront traffic beacon (see
    /// AnalyticsController) so already deployed Azure/Render databases pick it up without a full migration.</summary>
    private static async Task EnsureSiteHitsTableAsync(
        BaglyDbContext db,
        CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(db, "SiteHits", cancellationToken))
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE [dbo].[SiteHits]
            (
                [Id]            BIGINT IDENTITY(1,1) NOT NULL,
                [Path]          NVARCHAR(500)        NOT NULL,
                [OccurredAtUtc] DATETIME2            NOT NULL,
                [IpAddress]     NVARCHAR(64)         NULL,
                [Country]       NVARCHAR(100)        NOT NULL,
                [Region]        NVARCHAR(100)        NULL,
                [City]          NVARCHAR(100)        NULL,
                [UserAgent]     NVARCHAR(300)        NULL,
                [SessionId]     NVARCHAR(100)        NULL,
                CONSTRAINT [PK_SiteHits] PRIMARY KEY ([Id])
            );

            CREATE INDEX [IX_SiteHits_OccurredAtUtc] ON [dbo].[SiteHits] ([OccurredAtUtc]);
            CREATE INDEX [IX_SiteHits_Country] ON [dbo].[SiteHits] ([Country]);
            CREATE INDEX [IX_SiteHits_SessionId] ON [dbo].[SiteHits] ([SessionId]);
            """,
            cancellationToken);
    }

    private static async Task<bool> ColumnExistsAsync(
        BaglyDbContext db,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;

        if (shouldClose)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE WHEN COL_LENGTH(@tableName, @columnName) IS NULL THEN 0 ELSE 1 END
                """;

            var tableParam = command.CreateParameter();
            tableParam.ParameterName = "@tableName";
            tableParam.Value = $"dbo.{tableName}";
            command.Parameters.Add(tableParam);

            var columnParam = command.CreateParameter();
            columnParam.ParameterName = "@columnName";
            columnParam.Value = columnName;
            command.Parameters.Add(columnParam);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) == 1;
        }
        finally
        {
            if (shouldClose)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private static async Task<bool> TableExistsAsync(
        BaglyDbContext db,
        string tableName,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;

        if (shouldClose)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE WHEN OBJECT_ID(@tableName, N'U') IS NULL THEN 0 ELSE 1 END
                """;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tableName";
            parameter.Value = $"dbo.{tableName}";
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) == 1;
        }
        finally
        {
            if (shouldClose)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
