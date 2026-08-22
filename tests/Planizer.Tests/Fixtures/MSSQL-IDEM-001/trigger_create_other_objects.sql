-- Every CREATE that names a persistent object needs a guard: view, function, trigger, type,
-- schema, sequence.
-- expect: MSSQL-IDEM-001 severity=Warning line=9
-- expect: MSSQL-IDEM-001 severity=Warning line=12
-- expect: MSSQL-IDEM-001 severity=Warning line=15
-- expect: MSSQL-IDEM-001 severity=Warning line=18
-- expect: MSSQL-IDEM-001 severity=Warning line=20
-- expect: MSSQL-IDEM-001 severity=Warning line=22
CREATE VIEW dbo.OpenOrders AS SELECT Id FROM dbo.Orders WHERE Status = 1;
GO

CREATE FUNCTION dbo.OrderTotal (@Id int) RETURNS money AS BEGIN RETURN 0; END
GO

CREATE TRIGGER dbo.TR_Orders ON dbo.Orders AFTER INSERT AS SELECT 1;
GO

CREATE TYPE dbo.IdList AS TABLE (Id int NOT NULL);
GO
CREATE SCHEMA audit;
GO
CREATE SEQUENCE dbo.OrderNumbers AS int START WITH 1;
GO
