-- DDL inside an explicit transaction with no prior SET LOCK_TIMEOUT:
-- if the Sch-M lock is contended, the migration waits forever.
-- expect: MSSQL-LOCK-010 severity=Warning line=5
BEGIN TRAN;
ALTER TABLE dbo.T ADD C int NULL;
COMMIT;
