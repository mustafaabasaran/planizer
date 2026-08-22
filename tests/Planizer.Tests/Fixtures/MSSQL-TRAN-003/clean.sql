-- Each transaction is opened and committed inside one batch.
-- expect-none: MSSQL-TRAN-003
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
COMMIT;
GO
BEGIN TRAN;
ALTER TABLE dbo.B ADD C2 int NULL;
COMMIT;
GO
