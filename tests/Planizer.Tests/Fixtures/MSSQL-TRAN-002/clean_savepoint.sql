-- ROLLBACK to a savepoint does not end the transaction; the final COMMIT does.
-- expect-none: MSSQL-TRAN-002
BEGIN TRAN;
SAVE TRANSACTION BeforeCleanup;
DELETE FROM dbo.Staging WHERE LoadedAt < '2020-01-01';
IF @@ROWCOUNT > 1000
    ROLLBACK TRANSACTION BeforeCleanup;
COMMIT;
