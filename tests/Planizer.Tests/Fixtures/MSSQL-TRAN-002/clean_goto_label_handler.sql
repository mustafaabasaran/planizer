-- Label-based error handling: the main path commits and RETURNs; the ERR: block after the RETURN
-- is reached only through GOTO, so its ROLLBACK is the error path, not a stray ROLLBACK (3903).
-- expect-none: MSSQL-TRAN-002
BEGIN TRAN;
UPDATE dbo.A SET C1 = 1 WHERE Id = 1;
IF @@ERROR <> 0 GOTO ERR;
UPDATE dbo.B SET C2 = 1 WHERE Id = 1;
IF @@ERROR <> 0 GOTO ERR;
COMMIT;
RETURN;
ERR:
ROLLBACK;
RAISERROR('migration failed', 16, 1);
