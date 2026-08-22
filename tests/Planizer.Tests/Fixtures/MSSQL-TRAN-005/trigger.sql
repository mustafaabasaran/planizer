-- The CATCH block prints and moves on: the migration runner sees success for a half-applied script.
-- expect: MSSQL-TRAN-005 severity=Warning line=3
BEGIN TRY
    ALTER TABLE dbo.A ADD C1 int NULL;
END TRY
BEGIN CATCH
    PRINT ERROR_MESSAGE();
END CATCH
