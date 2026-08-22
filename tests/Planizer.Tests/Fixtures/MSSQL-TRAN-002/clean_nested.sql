-- Nested transactions: every BEGIN has its COMMIT.
-- expect-none: MSSQL-TRAN-002
BEGIN TRAN;
BEGIN TRAN;
UPDATE dbo.A SET C1 = 1 WHERE Id = 1;
COMMIT;
COMMIT;
