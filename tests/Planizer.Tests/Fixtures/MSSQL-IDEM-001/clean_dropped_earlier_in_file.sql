-- A safe DROP (IF EXISTS, or guarded by OBJECT_ID) earlier in the same file makes the CREATE re-runnable.
-- expect-none: MSSQL-IDEM-001
DROP TABLE IF EXISTS dbo.Staging;
CREATE TABLE dbo.Staging (Id int NOT NULL);

DROP INDEX IF EXISTS IX_Staging_Id ON dbo.Staging;
CREATE INDEX IX_Staging_Id ON dbo.Staging (Id);

DROP TYPE IF EXISTS dbo.IdList;
CREATE TYPE dbo.IdList AS TABLE (Id int NOT NULL);

IF OBJECT_ID(N'dbo.GetOrders', N'P') IS NOT NULL
    DROP PROCEDURE dbo.GetOrders;
GO
CREATE PROCEDURE dbo.GetOrders AS SELECT Id FROM dbo.Orders;
GO
IF SCHEMA_ID(N'audit') IS NOT NULL
    DROP SCHEMA audit;
GO
CREATE SCHEMA audit;
GO
