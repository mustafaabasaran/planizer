-- RAISERROR with severity 10 is an informational message, not an error: the batch continues and succeeds.
-- expect: MSSQL-TRAN-005 severity=Warning line=3
BEGIN TRY
    ALTER TABLE dbo.A ADD C1 int NULL;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    RAISERROR('migration step failed', 10, 1);
END CATCH
