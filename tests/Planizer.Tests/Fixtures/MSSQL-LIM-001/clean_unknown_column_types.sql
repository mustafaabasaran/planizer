-- The table is not created in this file, so the key width cannot be computed offline; the rule
-- stays silent rather than reporting inconclusive on every index (the column count is fine).
-- expect-none: MSSQL-LIM-001
CREATE INDEX IX_Orders_Customer ON dbo.Orders (CustomerId, OrderDate DESC) INCLUDE (Total);
ALTER TABLE dbo.Orders ADD CONSTRAINT UQ_Orders_Number UNIQUE NONCLUSTERED (OrderNumber);
