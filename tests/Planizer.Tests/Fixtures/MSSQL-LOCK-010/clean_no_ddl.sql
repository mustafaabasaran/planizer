-- A transaction without DDL is not this rule's concern.
-- expect-none: MSSQL-LOCK-010
BEGIN TRAN;
UPDATE dbo.T SET C = 1 WHERE Id = 5;
COMMIT;
