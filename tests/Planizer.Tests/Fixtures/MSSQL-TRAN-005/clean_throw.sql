-- THROW rethrows the original error.
-- expect-none: MSSQL-TRAN-005
BEGIN TRY
    ALTER TABLE dbo.A ADD C1 int NULL;
END TRY
BEGIN CATCH
    PRINT ERROR_MESSAGE();
    THROW;
END CATCH
