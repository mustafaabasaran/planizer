-- Both Sch-M statements hit the SAME table: no cross-table deadlock potential.
-- expect-none: MSSQL-LOCK-008
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
ALTER TABLE dbo.A ADD C2 int NULL;
COMMIT;
