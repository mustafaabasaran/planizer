-- expect: MSSQL-RW-011 severity=Warning line=2
ALTER TABLE dbo.Orders
ADD TotalPrice AS (Quantity * UnitPrice) PERSISTED;
