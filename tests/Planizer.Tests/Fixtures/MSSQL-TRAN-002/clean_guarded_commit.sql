-- A COMMIT guarded by @@TRANCOUNT cannot raise 3902, even with no BEGIN TRAN in this script.
-- expect-none: MSSQL-TRAN-002
ALTER TABLE dbo.A ADD C1 int NULL;
IF @@TRANCOUNT > 0 COMMIT;
