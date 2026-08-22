-- ROLLBACK nested deeper in the CATCH (XACT_STATE() check inside BEGIN...END) still counts.
-- expect-none: MSSQL-TRAN-004
BEGIN TRY
    BEGIN TRAN;
    ALTER TABLE dbo.A ADD C1 int NULL;
    COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
    BEGIN
        ROLLBACK TRANSACTION;
    END
    THROW;
END CATCH
