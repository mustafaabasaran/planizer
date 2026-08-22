-- BEGIN TRAN is never committed or rolled back: the transaction is still open when the script ends.
-- expect: MSSQL-TRAN-002 severity=Critical line=3
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
UPDATE dbo.A SET C1 = 0 WHERE C1 IS NULL;
