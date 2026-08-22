-- An IF that does not look at the catalog is not an existence guard.
-- expect: MSSQL-IDEM-001 severity=Warning line=5
DECLARE @env sysname = @@SERVERNAME;
IF @env = 'PROD'
    CREATE TABLE dbo.Orders (Id int NOT NULL);
