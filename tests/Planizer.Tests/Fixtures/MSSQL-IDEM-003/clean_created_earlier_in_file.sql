-- Dropping an object the same file created earlier is the staging pattern; a re-run recreates it first.
-- expect-none: MSSQL-IDEM-003
IF OBJECT_ID(N'dbo.Staging', N'U') IS NULL
    CREATE TABLE dbo.Staging (Id int NOT NULL);
CREATE INDEX IX_Staging_Id ON dbo.Staging (Id);
INSERT INTO dbo.Staging (Id) SELECT Id FROM dbo.Orders;
DROP INDEX IX_Staging_Id ON dbo.Staging;
DROP TABLE dbo.Staging;
