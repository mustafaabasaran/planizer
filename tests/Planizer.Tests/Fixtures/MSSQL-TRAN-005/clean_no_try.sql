-- No TRY/CATCH at all: errors propagate naturally.
-- expect-none: MSSQL-TRAN-005
ALTER TABLE dbo.A ADD C1 int NULL;
