-- Offline the script alone cannot tell whether the dropped index is clustered;
-- the rule must not stay silent — it reports an inconclusive Warning.
-- expect: MSSQL-RW-013 severity=Warning line=4
DROP INDEX IX_Orders_Code ON dbo.Orders;
