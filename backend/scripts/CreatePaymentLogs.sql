/*
  Bagly — PaymentLogs + Orders payment columns
  Safe to re-run.
*/
IF OBJECT_ID(N'dbo.PaymentLogs', N'U') IS NULL
BEGIN
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
END
GO

IF OBJECT_ID(N'dbo.Orders', N'U') IS NOT NULL
BEGIN
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
END
GO
