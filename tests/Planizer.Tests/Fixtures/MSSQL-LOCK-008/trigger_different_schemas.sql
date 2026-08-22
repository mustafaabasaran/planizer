-- Same table name in two schemas is two tables: the default-schema assumption only applies to
-- unqualified names.
-- expect: MSSQL-LOCK-008 severity=Warning line=5
BEGIN TRAN;
ALTER TABLE audit.T ADD C1 int NULL;
ALTER TABLE dbo.T ADD C2 int NULL;
COMMIT;
