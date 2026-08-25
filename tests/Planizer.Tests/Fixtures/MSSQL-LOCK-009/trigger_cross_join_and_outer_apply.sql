-- A cross join — explicit or written as a comma — pairs every row of both sides, and OUTER APPLY
-- keeps every left row even when the right side returns none. Neither restricts the target.
-- expect: MSSQL-LOCK-009 severity=Warning line=6
-- expect: MSSQL-LOCK-009 severity=Warning line=7
-- expect: MSSQL-LOCK-009 severity=Warning line=8
DELETE t FROM dbo.Orders t CROSS JOIN dbo.Customers c;
DELETE t FROM dbo.Orders t, dbo.Customers c;
DELETE t FROM dbo.Orders t OUTER APPLY (SELECT TOP (1) c.Id AS Id FROM dbo.Customers c WHERE c.Id = t.CustomerId) x;
