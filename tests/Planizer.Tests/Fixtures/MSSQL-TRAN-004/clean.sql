-- CATCH rolls back conditionally before rethrowing.
-- expect-none: MSSQL-TRAN-004
BEGIN TRY
    BEGIN TRAN;
    ALTER TABLE dbo.A ADD C1 int NULL;
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
