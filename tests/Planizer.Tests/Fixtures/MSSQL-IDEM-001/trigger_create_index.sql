-- An unguarded CREATE INDEX fails on re-run: "The operation failed because an index or statistics
-- with name 'IX_Orders_Total' already exists" (1913).
-- expect: MSSQL-IDEM-001 severity=Warning line=4
CREATE INDEX IX_Orders_Total ON dbo.Orders (Total);
