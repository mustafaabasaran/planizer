-- Each Sch-M statement commits on its own: no accumulated blocking window.
-- expect-none: MSSQL-LOCK-007
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
COMMIT;
BEGIN TRAN;
ALTER TABLE dbo.B ADD C2 int NULL;
COMMIT;
