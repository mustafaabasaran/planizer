-- Same problem when the closing statement is a ROLLBACK two batches later.
-- expect: MSSQL-TRAN-003 severity=Warning line=3
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
GO
ALTER TABLE dbo.B ADD C2 int NULL;
GO
ROLLBACK;
