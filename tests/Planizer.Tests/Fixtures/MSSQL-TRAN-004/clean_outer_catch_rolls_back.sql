-- The inner CATCH only rethrows; the enclosing CATCH owns the rollback.
-- expect-none: MSSQL-TRAN-004
BEGIN TRY
    BEGIN TRAN;
    BEGIN TRY
        ALTER TABLE dbo.A ADD C1 int NULL;
    END TRY
    BEGIN CATCH
        PRINT 'inner';
        THROW;
    END CATCH
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH
