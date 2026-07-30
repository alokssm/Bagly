-- Fix IIS 500.30 for Bagly.Api:
-- Login failed for user 'IIS APPPOOL\BaglyApiAppPool' against BaglyDb.
-- Run in SSMS or: sqlcmd -S "localhost\SQLEXPRESS" -E -C -i Grant-IisAppPoolSqlAccess.sql

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'IIS APPPOOL\BaglyApiAppPool')
BEGIN
    CREATE LOGIN [IIS APPPOOL\BaglyApiAppPool] FROM WINDOWS;
END
GO

USE [BaglyDb];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IIS APPPOOL\BaglyApiAppPool')
BEGIN
    CREATE USER [IIS APPPOOL\BaglyApiAppPool] FOR LOGIN [IIS APPPOOL\BaglyApiAppPool];
END
GO

ALTER ROLE [db_owner] ADD MEMBER [IIS APPPOOL\BaglyApiAppPool];
GO

PRINT 'IIS APPPOOL\BaglyApiAppPool granted access to BaglyDb.';
GO
