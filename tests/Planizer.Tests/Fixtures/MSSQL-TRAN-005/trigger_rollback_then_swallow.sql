-- Rolling back is not enough: without a rethrow the runner records the script as applied.
-- expect: MSSQL-TRAN-005 severity=Warning line=4
-- expect-none: MSSQL-TRAN-004
BEGIN TRY
    BEGIN TRAN;
    ALTER TABLE dbo.A ADD C1 int NULL;
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    INSERT INTO dbo.MigrationLog (Message) VALUES (ERROR_MESSAGE());
END CATCH
