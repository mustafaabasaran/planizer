-- The early-exit idiom: a catalog check that RETURNs (or THROWs / GOTOs out) when the object exists
-- guards every statement after it in the same batch, including ones nested in TRY / BEGIN…END.
-- expect-none: MSSQL-IDEM-001
IF OBJECT_ID(N'dbo.Orders', N'U') IS NOT NULL RETURN;
CREATE TABLE dbo.Orders (Id int NOT NULL PRIMARY KEY);
BEGIN TRY
    CREATE INDEX IX_Orders_Id ON dbo.Orders (Id);
END TRY
BEGIN CATCH
    THROW;
END CATCH
GO
IF TYPE_ID(N'dbo.IdList') IS NOT NULL
BEGIN
    PRINT 'already there';
    RETURN;
END
CREATE TYPE dbo.IdList AS TABLE (Id int NOT NULL);
GO
IF EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'OrderNumbers') THROW 50000, 'already applied', 1;
CREATE SEQUENCE dbo.OrderNumbers AS int START WITH 1;
