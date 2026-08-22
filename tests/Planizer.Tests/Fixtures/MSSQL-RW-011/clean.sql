-- expect-none: MSSQL-RW-011
ALTER TABLE dbo.Orders
ADD TotalPrice AS (Quantity * UnitPrice);
