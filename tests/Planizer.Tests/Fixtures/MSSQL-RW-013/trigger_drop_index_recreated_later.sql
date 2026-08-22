-- Drop-and-recreate: the CREATE comes AFTER the drop, so it proves nothing about the index being
-- dropped — the drop stays an inconclusive Warning.
-- expect: MSSQL-RW-013 severity=Warning line=4
DROP INDEX IX_Orders_Code ON dbo.Orders;
GO
CREATE NONCLUSTERED INDEX IX_Orders_Code ON dbo.Orders (Code);
