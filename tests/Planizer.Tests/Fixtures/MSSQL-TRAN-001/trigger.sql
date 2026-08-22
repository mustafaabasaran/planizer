-- Explicit transaction with no SET XACT_ABORT ON anywhere before it: a run-time error leaves it open.
-- expect: MSSQL-TRAN-001 severity=Warning line=3
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
COMMIT;
