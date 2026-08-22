-- ROLLBACK TRANSACTION <savepoint> is not a rollback of the transaction: it stays open — and when
-- the error has doomed it (XACT_STATE() = -1) the savepoint rollback itself fails with error 3931.
-- expect: MSSQL-TRAN-004 severity=Critical line=5
BEGIN TRY
    BEGIN TRAN;
    SAVE TRANSACTION BeforeUpdate;
    UPDATE dbo.A SET C1 = 1 WHERE Id = 1;
    COMMIT;
END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION BeforeUpdate;
    THROW;
END CATCH
