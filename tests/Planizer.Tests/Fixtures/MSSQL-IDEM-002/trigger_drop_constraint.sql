-- Re-running an unguarded DROP CONSTRAINT fails: "'FK_Orders_Customers' is not a constraint" (3728).
-- expect: MSSQL-IDEM-002 severity=Warning line=3
ALTER TABLE dbo.Orders DROP CONSTRAINT FK_Orders_Customers;
