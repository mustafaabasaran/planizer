-- The BEGIN TRAN sits in an IF inside the TRY; it still belongs to that TRY's CATCH.
-- expect: MSSQL-TRAN-004 severity=Critical line=6
BEGIN TRY
    IF OBJECT_ID('dbo.A') IS NOT NULL
    BEGIN
        BEGIN TRAN;
        ALTER TABLE dbo.A ADD C1 int NULL;
        COMMIT;
    END
END TRY
BEGIN CATCH
    PRINT ERROR_MESSAGE();
    THROW;
END CATCH
