-- SET options persist for the session, so an ON in an earlier batch still covers the transaction.
-- expect-none: MSSQL-TRAN-001
SET XACT_ABORT ON;
GO
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
COMMIT;
