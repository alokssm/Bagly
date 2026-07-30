/*
  Create logging tables for Bagly
  - AuditLogs: business/audit events (login, product/category changes, errors)
  - Logs: created automatically by Serilog.Sinks.MSSqlServer (AutoCreateSqlTable)
*/

USE BaglyDb;
GO

IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
BEGIN
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
END
GO

PRINT 'AuditLogs ready. Serilog will create dbo.Logs on API startup.';
GO
