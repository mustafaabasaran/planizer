-- THROW inside a nested IF in the CATCH still rethrows on that path.
-- expect-none: MSSQL-TRAN-005
BEGIN TRY
    ALTER TABLE dbo.A ADD C1 int NULL;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 2705
    BEGIN
        THROW;
    END
END CATCH
