-- Existence checks in the enclosing IF (either branch, through BEGIN-END) make the CREATE re-runnable.
-- expect-none: MSSQL-IDEM-001
IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Orders (Id int NOT NULL PRIMARY KEY);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_Total' AND object_id = OBJECT_ID(N'dbo.Orders'))
    CREATE INDEX IX_Orders_Total ON dbo.Orders (Total);

IF EXISTS (SELECT 1 FROM sys.types WHERE name = N'IdList' AND schema_id = SCHEMA_ID(N'dbo'))
    PRINT 'type exists';
ELSE
    CREATE TYPE dbo.IdList AS TABLE (Id int NOT NULL);

IF SCHEMA_ID(N'audit') IS NULL
    EXEC(N'CREATE SCHEMA audit');

IF OBJECT_ID(N'dbo.OrderNumbers', N'SO') IS NULL
    CREATE SEQUENCE dbo.OrderNumbers AS int START WITH 1;
