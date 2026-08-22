-- Two transactions, no XACT_ABORT: one finding per file, anchored to the first BEGIN TRAN (line 3).
-- expect: MSSQL-TRAN-001 severity=Warning line=3
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
COMMIT;
BEGIN TRAN;
ALTER TABLE dbo.B ADD C2 int NULL;
COMMIT;
