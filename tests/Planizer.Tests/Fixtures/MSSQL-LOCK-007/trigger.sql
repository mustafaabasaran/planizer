-- Two Sch-M statements in one transaction: both locks are held until COMMIT.
-- expect: MSSQL-LOCK-007 severity=Critical line=4
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
ALTER TABLE dbo.B ADD C2 int NULL;
COMMIT;
