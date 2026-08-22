-- Outside a transaction: no rollback window at all; FK references unknowable offline.
-- expect: MSSQL-REV-004 severity=Warning line=3
TRUNCATE TABLE dbo.Staging;
