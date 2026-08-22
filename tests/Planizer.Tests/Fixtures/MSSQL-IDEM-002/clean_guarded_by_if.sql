-- COL_LENGTH / sys.columns / sys.objects checks make every ALTER TABLE element change re-runnable.
-- expect-none: MSSQL-IDEM-002
IF COL_LENGTH(N'dbo.Orders', N'Status') IS NULL
    ALTER TABLE dbo.Orders ADD Status tinyint NULL;

IF COL_LENGTH(N'dbo.Orders', N'LegacyCode') IS NOT NULL
    ALTER TABLE dbo.Orders DROP COLUMN LegacyCode;

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = N'CK_Orders_Total' AND parent_object_id = OBJECT_ID(N'dbo.Orders'))
    ALTER TABLE dbo.Orders ADD CONSTRAINT CK_Orders_Total CHECK (Total >= 0);

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Orders_Customers')
BEGIN
    ALTER TABLE dbo.Orders DROP CONSTRAINT FK_Orders_Customers;
END
