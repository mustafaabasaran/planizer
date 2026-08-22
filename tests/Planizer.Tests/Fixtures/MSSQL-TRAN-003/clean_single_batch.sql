-- No GO at all: nothing to span.
-- expect-none: MSSQL-TRAN-003
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
ALTER TABLE dbo.B ADD C2 int NULL;
COMMIT;
