-- Six transactions each open in one batch and commit two batches later (the EF Core idempotent
-- script shape). Above the per-file threshold (5) the rule reports once, anchored at the first
-- BEGIN TRAN, with the count and the first examples.
-- expect: MSSQL-TRAN-003 severity=Warning line=5
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
BEGIN TRANSACTION;
GO
ALTER TABLE dbo.A ADD C6 int NULL;
GO
COMMIT;
GO
