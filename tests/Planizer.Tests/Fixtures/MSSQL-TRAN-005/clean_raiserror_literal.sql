-- RAISERROR with severity 16 fails the batch.
-- expect-none: MSSQL-TRAN-005
BEGIN TRY
    ALTER TABLE dbo.A ADD C1 int NULL;
END TRY
BEGIN CATCH
    RAISERROR('migration step failed', 16, 1);
END CATCH
