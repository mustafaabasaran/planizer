-- An empty CATCH swallows every error.
-- expect: MSSQL-TRAN-005 severity=Warning line=3
BEGIN TRY
    ALTER TABLE dbo.A ADD C1 int NULL;
END TRY
BEGIN CATCH
END CATCH
