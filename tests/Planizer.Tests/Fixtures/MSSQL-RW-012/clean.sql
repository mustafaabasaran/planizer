-- expect-none: MSSQL-RW-012
ALTER TABLE dbo.BigTable REBUILD;
ALTER INDEX IX_BigTable_Code ON dbo.BigTable REBUILD;
