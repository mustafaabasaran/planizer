-- DROP … IF EXISTS (2016+) never fails on a missing object.
-- expect-none: MSSQL-IDEM-003
DROP TABLE IF EXISTS dbo.Legacy;
DROP INDEX IF EXISTS IX_Orders_Total ON dbo.Orders;
DROP VIEW IF EXISTS dbo.OpenOrders;
DROP PROCEDURE IF EXISTS dbo.GetOrders;
DROP FUNCTION IF EXISTS dbo.OrderTotal;
DROP TRIGGER IF EXISTS dbo.TR_Orders;
DROP TYPE IF EXISTS dbo.IdList;
DROP SEQUENCE IF EXISTS dbo.OrderNumbers;
DROP SCHEMA IF EXISTS audit;
