-- SET XACT_ABORT ON precedes the transaction: any error rolls it back and aborts the batch.
-- expect-none: MSSQL-TRAN-001
SET XACT_ABORT ON;
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
COMMIT;
