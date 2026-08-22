-- Migration 001: add order-status tracking to dbo.Orders.
-- Adds a Status column with a default, a nullable change-timestamp column,
-- backfills shipped orders, and creates a supporting index.

SET XACT_ABORT ON;
SET LOCK_TIMEOUT 30000;

BEGIN TRANSACTION;

ALTER TABLE dbo.Orders
    ADD Status tinyint NOT NULL CONSTRAINT DF_Orders_Status DEFAULT (0);

ALTER TABLE dbo.Orders
    ADD StatusChangedAt datetime2(3) NULL;

COMMIT TRANSACTION;
GO  -- the backfill references the new column: it must compile in a later batch (MSSQL-BATCH-001)

UPDATE dbo.Orders
SET Status = 3
WHERE ShippedDate IS NOT NULL;

CREATE NONCLUSTERED INDEX IX_Orders_Status
    ON dbo.Orders (Status)
    INCLUDE (CustomerId);
