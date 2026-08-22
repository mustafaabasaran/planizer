-- Inside a transaction TRUNCATE can still be rolled back until COMMIT.
-- expect: MSSQL-REV-004 severity=Warning line=4
BEGIN TRAN;
TRUNCATE TABLE dbo.Staging;
COMMIT;
