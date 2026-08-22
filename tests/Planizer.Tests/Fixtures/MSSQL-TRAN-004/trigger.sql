-- BEGIN TRAN inside TRY, but the CATCH block never rolls back: an error leaves a doomed transaction open.
-- expect: MSSQL-TRAN-004 severity=Critical line=4
BEGIN TRY
    BEGIN TRAN;
    ALTER TABLE dbo.A ADD C1 int NULL;
    COMMIT;
END TRY
BEGIN CATCH
    THROW;
END CATCH
