-- No explicit transaction: each statement autocommits and releases its lock immediately.
-- expect-none: MSSQL-LOCK-007
ALTER TABLE dbo.A ADD C1 int NULL;
ALTER TABLE dbo.B ADD C2 int NULL;
