-- Plain CREATE PROCEDURE is not re-runnable; CREATE OR ALTER (2016 SP1+) is.
-- expect: MSSQL-IDEM-001 severity=Warning line=3
CREATE PROCEDURE dbo.GetOrders
AS
BEGIN
    SELECT Id FROM dbo.Orders;
END
