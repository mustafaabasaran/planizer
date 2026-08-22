-- The only way out of the transaction is the CATCH block; on the success path it is never committed.
-- expect: MSSQL-TRAN-002 severity=Critical line=4
BEGIN TRY
    BEGIN TRAN;
    ALTER TABLE dbo.A ADD C1 int NULL;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH
