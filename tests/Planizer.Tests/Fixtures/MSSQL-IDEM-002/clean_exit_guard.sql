-- The early-exit idiom guards ALTER TABLE too: a catalog check that RETURNs when the column
-- (or constraint) is already there makes the bare ADD / DROP after it re-runnable.
-- expect-none: MSSQL-IDEM-002
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Orders') AND name = N'Status') RETURN;
ALTER TABLE dbo.Orders ADD Status tinyint NULL;
ALTER TABLE dbo.Orders ADD CONSTRAINT CK_Orders_Status CHECK (Status BETWEEN 0 AND 5);
GO
IF COL_LENGTH(N'dbo.Orders', N'LegacyCode') IS NULL RETURN;
ALTER TABLE dbo.Orders DROP COLUMN LegacyCode;
