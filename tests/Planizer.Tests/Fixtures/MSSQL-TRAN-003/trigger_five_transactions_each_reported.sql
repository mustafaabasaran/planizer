-- Five spanning transactions are still reported one by one (the per-file aggregate starts at six).
-- expect: MSSQL-TRAN-003 severity=Warning line=7
-- expect: MSSQL-TRAN-003 severity=Warning line=13
-- expect: MSSQL-TRAN-003 severity=Warning line=19
-- expect: MSSQL-TRAN-003 severity=Warning line=25
-- expect: MSSQL-TRAN-003 severity=Warning line=31
BEGIN TRANSACTION;
GO
ALTER TABLE dbo.A ADD C1 int NULL;
GO
COMMIT;
GO
BEGIN TRANSACTION;
GO
ALTER TABLE dbo.A ADD C2 int NULL;
GO
COMMIT;
GO
BEGIN TRANSACTION;
GO
ALTER TABLE dbo.A ADD C3 int NULL;
GO
COMMIT;
GO
BEGIN TRANSACTION;
GO
ALTER TABLE dbo.A ADD C4 int NULL;
GO
COMMIT;
GO
BEGIN TRANSACTION;
GO
ALTER TABLE dbo.A ADD C5 int NULL;
GO
COMMIT;
GO
