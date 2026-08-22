-- View, procedure, function, trigger, type, sequence and schema drops all need a guard.
-- expect: MSSQL-IDEM-003 severity=Warning line=9
-- expect: MSSQL-IDEM-003 severity=Warning line=10
-- expect: MSSQL-IDEM-003 severity=Warning line=11
-- expect: MSSQL-IDEM-003 severity=Warning line=12
-- expect: MSSQL-IDEM-003 severity=Warning line=13
-- expect: MSSQL-IDEM-003 severity=Warning line=14
-- expect: MSSQL-IDEM-003 severity=Warning line=15
DROP VIEW dbo.OpenOrders;
DROP PROCEDURE dbo.GetOrders;
DROP FUNCTION dbo.OrderTotal;
DROP TRIGGER dbo.TR_Orders;
DROP TYPE dbo.IdList;
DROP SEQUENCE dbo.OrderNumbers;
DROP SCHEMA audit;
