-- COMMIT with no open transaction: error 3902 at run time.
-- expect: MSSQL-TRAN-002 severity=Critical line=4
ALTER TABLE dbo.A ADD C1 int NULL;
COMMIT;
