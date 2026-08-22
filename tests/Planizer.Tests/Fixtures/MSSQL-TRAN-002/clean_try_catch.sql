-- The canonical shape: COMMIT on the success path, ROLLBACK in CATCH, error rethrown.
-- expect-none: MSSQL-TRAN-002
-- expect-none: MSSQL-TRAN-004
-- expect-none: MSSQL-TRAN-005
-- expect-none: MSSQL-TRAN-001
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRAN;
    ALTER TABLE dbo.A ADD C1 int NULL;
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
