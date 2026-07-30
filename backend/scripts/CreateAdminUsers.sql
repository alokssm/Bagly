/*
  Create AdminUsers table for Bagly admin login
*/

USE BaglyDb;
GO

IF OBJECT_ID(N'dbo.AdminUsers', N'U') IS NULL
BEGIN
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
END
GO

PRINT 'AdminUsers table is ready. Default admin is seeded by the API on startup.';
GO
