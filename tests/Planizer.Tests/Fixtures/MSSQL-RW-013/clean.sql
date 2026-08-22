-- expect-none: MSSQL-RW-013
CREATE NONCLUSTERED INDEX IX_Orders_Code ON dbo.Orders (Code);
