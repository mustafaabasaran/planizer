-- expect: MSSQL-RW-001 severity=Info line=3
-- expect: MSSQL-RW-001 severity=Info line=4
ALTER TABLE dbo.Orders ADD Notes nvarchar(500) NULL;
ALTER TABLE dbo.Orders ADD ExternalRef int;
