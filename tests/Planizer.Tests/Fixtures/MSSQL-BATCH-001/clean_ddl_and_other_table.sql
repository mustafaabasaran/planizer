-- DDL binds columns at execution (index, constraint, ALTER COLUMN), dynamic SQL compiles later,
-- and a same-named column on another table or through another table's alias is not this column.
-- expect-none: MSSQL-BATCH-001
ALTER TABLE dbo.Orders ADD Status tinyint NULL;
CREATE INDEX IX_Orders_Status ON dbo.Orders (Status);
ALTER TABLE dbo.Orders ADD CONSTRAINT CK_Orders_Status CHECK (Status BETWEEN 0 AND 5);
ALTER TABLE dbo.Orders ALTER COLUMN Status tinyint NOT NULL;
EXEC sp_executesql N'UPDATE dbo.Orders SET Status = 3 WHERE ShippedDate IS NOT NULL';
UPDATE dbo.Customers SET Status = 1 WHERE Id = 1;
SELECT c.Status FROM dbo.Orders o JOIN dbo.Customers c ON c.Id = o.CustomerId;
