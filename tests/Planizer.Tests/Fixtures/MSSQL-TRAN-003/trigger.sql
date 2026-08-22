-- The transaction opens in batch 0 and commits in batch 1: if the second batch fails the transaction stays open.
-- expect: MSSQL-TRAN-003 severity=Warning line=3
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
GO
ALTER TABLE dbo.B ADD C2 int NULL;
COMMIT;
