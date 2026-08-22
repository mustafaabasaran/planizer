-- OBJECT_ID / sys.indexes / TYPE_ID / SCHEMA_ID guards make a plain DROP safe on any version.
-- expect-none: MSSQL-IDEM-003
IF OBJECT_ID(N'dbo.Legacy', N'U') IS NOT NULL
    DROP TABLE dbo.Legacy;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_Total' AND object_id = OBJECT_ID(N'dbo.Orders'))
BEGIN
    DROP INDEX IX_Orders_Total ON dbo.Orders;
END

IF TYPE_ID(N'dbo.IdList') IS NOT NULL
    DROP TYPE dbo.IdList;

IF SCHEMA_ID(N'audit') IS NOT NULL
    DROP SCHEMA audit;
