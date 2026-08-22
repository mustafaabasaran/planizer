-- expect-none: MSSQL-RW-003
ALTER TABLE dbo.Orders ADD Code int NOT NULL DEFAULT 0;
ALTER TABLE dbo.Orders ADD Memo nvarchar(100) NULL;
