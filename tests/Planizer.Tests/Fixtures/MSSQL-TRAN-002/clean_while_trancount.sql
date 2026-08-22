-- Clearing stale transactions at the top of a script: the ROLLBACK only runs while @@TRANCOUNT > 0,
-- so it can never raise 3903.
-- expect-none: MSSQL-TRAN-002
WHILE @@TRANCOUNT > 0 ROLLBACK;
BEGIN TRAN;
UPDATE dbo.A SET C1 = 1 WHERE Id = 1;
COMMIT;
