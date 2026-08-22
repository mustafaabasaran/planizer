-- XACT_ABORT is switched ON and then OFF again before the transaction opens: the last setting wins.
-- expect: MSSQL-TRAN-001 severity=Warning line=5
SET XACT_ABORT ON;
SET XACT_ABORT OFF;
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
COMMIT;
