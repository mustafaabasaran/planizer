-- No transaction opened inside the TRY: nothing for the CATCH to roll back.
-- expect-none: MSSQL-TRAN-004
BEGIN TRY
    ALTER TABLE dbo.A ADD C1 int NULL;
END TRY
BEGIN CATCH
    THROW;
END CATCH
