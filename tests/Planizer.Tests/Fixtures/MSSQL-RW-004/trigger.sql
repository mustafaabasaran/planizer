-- expect: MSSQL-RW-004 severity=Critical line=2
ALTER TABLE dbo.Orders ALTER COLUMN Notes nvarchar(MAX);
