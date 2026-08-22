-- No explicit transaction: nothing can be left open.
-- expect-none: MSSQL-TRAN-001
ALTER TABLE dbo.A ADD C1 int NULL;
ALTER TABLE dbo.B ADD C2 int NULL;
