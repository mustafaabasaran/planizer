-- Migration 004: customer contact preferences (re-runnable).
-- Every step checks the catalog first, so the script can be re-applied after a partial
-- failure or on an environment that already has some of the objects. The backfill sits in
-- its own batch because it references a column added just before it, and the transaction
-- is wrapped in TRY/CATCH that rolls back and re-throws.

SET XACT_ABORT ON;
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET LOCK_TIMEOUT 30000;
GO

IF OBJECT_ID(N'dbo.CustomerContactPreference', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerContactPreference
    (
        Id          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_CustomerContactPreference PRIMARY KEY,
        CustomerId  int          NOT NULL,
        Channel     tinyint      NOT NULL,
        IsOptedIn   bit          NOT NULL CONSTRAINT DF_CustomerContactPreference_IsOptedIn DEFAULT (1),
        UpdatedAt   datetime2(3) NOT NULL CONSTRAINT DF_CustomerContactPreference_UpdatedAt DEFAULT (SYSUTCDATETIME())
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CustomerContactPreference_Customer')
    ALTER TABLE dbo.CustomerContactPreference WITH CHECK
        ADD CONSTRAINT FK_CustomerContactPreference_Customer
            FOREIGN KEY (CustomerId) REFERENCES dbo.Customers (Id);
GO

IF COL_LENGTH(N'dbo.Customers', N'PreferredChannel') IS NULL
    ALTER TABLE dbo.Customers ADD PreferredChannel tinyint NULL;
GO  -- the backfill below references PreferredChannel: it must compile in a later batch

BEGIN TRY
    BEGIN TRANSACTION;

    UPDATE c
    SET    c.PreferredChannel = 1
    FROM   dbo.Customers AS c
    WHERE  c.PreferredChannel IS NULL
      AND  c.Email IS NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM dbo.CustomerContactPreference WHERE Channel = 1)
        INSERT INTO dbo.CustomerContactPreference (CustomerId, Channel, IsOptedIn)
        SELECT Id, 1, 1
        FROM   dbo.Customers
        WHERE  Email IS NOT NULL;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_CustomerContactPreference_CustomerId'
                 AND object_id = OBJECT_ID(N'dbo.CustomerContactPreference'))
    CREATE NONCLUSTERED INDEX IX_CustomerContactPreference_CustomerId
        ON dbo.CustomerContactPreference (CustomerId, Channel)
        INCLUDE (IsOptedIn);
GO

CREATE OR ALTER VIEW dbo.CustomerOptIns
AS
SELECT p.CustomerId, p.Channel, p.UpdatedAt
FROM   dbo.CustomerContactPreference AS p
WHERE  p.IsOptedIn = 1;
GO
