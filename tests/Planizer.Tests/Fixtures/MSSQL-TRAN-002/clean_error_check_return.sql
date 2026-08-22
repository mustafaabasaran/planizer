-- A branch that rolls back and RETURNs leaves the script; the COMMIT after it is on the other path.
-- expect-none: MSSQL-TRAN-002
BEGIN TRAN;
UPDATE dbo.A SET C1 = 1 WHERE Id = 1;
IF @@ERROR <> 0
BEGIN
    ROLLBACK;
    RETURN;
END
UPDATE dbo.B SET C2 = 1 WHERE Id = 1;
COMMIT;
