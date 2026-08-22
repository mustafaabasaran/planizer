-- expect: MSSQL-RW-009 severity=Critical line=2
ALTER TABLE dbo.Orders ALTER COLUMN CustomerName nvarchar(200) COLLATE Latin1_General_100_CI_AS;
