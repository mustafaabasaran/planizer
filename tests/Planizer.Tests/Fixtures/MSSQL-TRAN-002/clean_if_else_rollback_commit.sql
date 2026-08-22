-- Old-style error handling: the two branches are alternative paths, each closes the transaction once.
-- expect-none: MSSQL-TRAN-002
BEGIN TRAN;
UPDATE dbo.A SET C1 = 1 WHERE Id = 1;
IF @@ERROR <> 0
    ROLLBACK;
ELSE
    COMMIT;
