/*
  Bagly e-commerce schema for SQL Server
  Server example: localhost\SQLEXPRESS
  Database: BaglyDb
*/

IF DB_ID(N'BaglyDb') IS NULL
BEGIN
    CREATE DATABASE BaglyDb;
END
GO

USE BaglyDb;
GO

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[__EFMigrationsHistory]
    (
        [MigrationId]    NVARCHAR(150) NOT NULL,
        [ProductVersion] NVARCHAR(32)  NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END
GO

IF OBJECT_ID(N'dbo.Categories', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Categories]
    (
        [Id]        NVARCHAR(50)  NOT NULL,
        [Label]     NVARCHAR(100) NOT NULL,
        [SortOrder] INT           NOT NULL,
        CONSTRAINT [PK_Categories] PRIMARY KEY ([Id])
    );
END
GO

IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Products]
    (
        [Id]               NVARCHAR(100)   NOT NULL,
        [Name]             NVARCHAR(200)   NOT NULL,
        [Category]         NVARCHAR(50)    NOT NULL,
        [Price]            DECIMAL(18, 2)  NOT NULL,
        [CompareAt]        DECIMAL(18, 2)  NULL,
        [Material]         NVARCHAR(100)   NOT NULL,
        [Rating]           FLOAT           NOT NULL,
        [Reviews]          INT             NOT NULL,
        [Badge]            NVARCHAR(50)    NULL,
        [ShortDescription] NVARCHAR(500)   NOT NULL,
        [Description]      NVARCHAR(4000)  NOT NULL,
        [Image]            NVARCHAR(1000)  NOT NULL,
        [ColorsJson]       NVARCHAR(MAX)   NOT NULL,
        [FeaturesJson]     NVARCHAR(MAX)   NOT NULL,
        [GalleryJson]      NVARCHAR(MAX)   NOT NULL,
        [IsActive]         BIT             NOT NULL CONSTRAINT [DF_Products_IsActive] DEFAULT (1),
        [CreatedAt]        DATETIME2       NOT NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([Id])
    );

    CREATE INDEX [IX_Products_Category] ON [dbo].[Products] ([Category]);
    CREATE INDEX [IX_Products_IsActive] ON [dbo].[Products] ([IsActive]);
END
GO

IF OBJECT_ID(N'dbo.Carts', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Carts]
    (
        [Id]        UNIQUEIDENTIFIER NOT NULL,
        [UpdatedAt] DATETIME2        NOT NULL,
        CONSTRAINT [PK_Carts] PRIMARY KEY ([Id])
    );
END
GO

IF OBJECT_ID(N'dbo.CartItems', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CartItems]
    (
        [Id]          INT              IDENTITY(1,1) NOT NULL,
        [CartId]      UNIQUEIDENTIFIER NOT NULL,
        [ProductId]   NVARCHAR(100)    NOT NULL,
        [ProductName] NVARCHAR(200)    NOT NULL,
        [Image]       NVARCHAR(1000)   NOT NULL,
        [Color]       NVARCHAR(50)     NOT NULL,
        [UnitPrice]   DECIMAL(18, 2)   NOT NULL,
        [Quantity]    INT              NOT NULL,
        CONSTRAINT [PK_CartItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CartItems_Carts_CartId]
            FOREIGN KEY ([CartId]) REFERENCES [dbo].[Carts] ([Id]) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX [IX_CartItems_CartId_ProductId_Color]
        ON [dbo].[CartItems] ([CartId], [ProductId], [Color]);
END
GO

IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Orders]
    (
        [Id]          UNIQUEIDENTIFIER NOT NULL,
        [OrderNumber] NVARCHAR(50)     NOT NULL,
        [Email]       NVARCHAR(256)    NOT NULL,
        [FirstName]   NVARCHAR(100)    NOT NULL,
        [LastName]    NVARCHAR(100)    NOT NULL,
        [Address]     NVARCHAR(300)    NOT NULL,
        [City]        NVARCHAR(100)    NOT NULL,
        [State]       NVARCHAR(100)    NOT NULL,
        [Zip]         NVARCHAR(20)     NOT NULL,
        [Country]     NVARCHAR(100)    NOT NULL,
        [Subtotal]    DECIMAL(18, 2)   NOT NULL,
        [Shipping]    DECIMAL(18, 2)   NOT NULL,
        [Total]       DECIMAL(18, 2)   NOT NULL,
        [Status]      NVARCHAR(50)     NOT NULL,
        [CreatedAt]   DATETIME2        NOT NULL,
        CONSTRAINT [PK_Orders] PRIMARY KEY ([Id])
    );

    CREATE UNIQUE INDEX [IX_Orders_OrderNumber] ON [dbo].[Orders] ([OrderNumber]);
    CREATE INDEX [IX_Orders_Email] ON [dbo].[Orders] ([Email]);
    CREATE INDEX [IX_Orders_CreatedAt] ON [dbo].[Orders] ([CreatedAt]);
END
GO

IF OBJECT_ID(N'dbo.OrderItems', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[OrderItems]
    (
        [Id]          INT              IDENTITY(1,1) NOT NULL,
        [OrderId]     UNIQUEIDENTIFIER NOT NULL,
        [ProductId]   NVARCHAR(100)    NOT NULL,
        [ProductName] NVARCHAR(200)    NOT NULL,
        [Color]       NVARCHAR(50)     NOT NULL,
        [UnitPrice]   DECIMAL(18, 2)   NOT NULL,
        [Quantity]    INT              NOT NULL,
        CONSTRAINT [PK_OrderItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderItems_Orders_OrderId]
            FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Orders] ([Id]) ON DELETE CASCADE
    );
END
GO

-- Keep EF Core in sync when schema is created from this script.
IF NOT EXISTS (
    SELECT 1 FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729121148_InitialCreate')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260729121148_InitialCreate', N'8.0.11');
END
GO
