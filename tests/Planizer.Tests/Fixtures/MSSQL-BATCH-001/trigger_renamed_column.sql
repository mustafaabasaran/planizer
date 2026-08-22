-- sp_rename gives the column its new name only when it executes; the SELECT on line 5 is
-- compiled with the batch and still sees the old name.
-- expect: MSSQL-BATCH-001 severity=Blocker line=5
EXEC sp_rename 'dbo.Customers.Fax', 'FaxNumber', 'COLUMN';
SELECT FaxNumber FROM dbo.Customers WHERE Id = 1;
