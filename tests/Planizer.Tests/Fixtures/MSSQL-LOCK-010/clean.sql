-- SET LOCK_TIMEOUT appears before the transactional DDL: waits are bounded.
-- expect-none: MSSQL-LOCK-010
SET LOCK_TIMEOUT 30000;
BEGIN TRAN;
ALTER TABLE dbo.T ADD C int NULL;
COMMIT;
