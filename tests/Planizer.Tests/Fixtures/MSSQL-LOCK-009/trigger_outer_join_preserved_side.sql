-- An outer join preserves its outer side in full: every row of the target survives the join, so
-- this is a full-table write even though the FROM clause holds a JOIN.
-- expect: MSSQL-LOCK-009 severity=Warning line=7
-- expect: MSSQL-LOCK-009 severity=Warning line=8
-- expect: MSSQL-LOCK-009 severity=Warning line=9
-- expect: MSSQL-LOCK-009 severity=Warning line=10
DELETE t FROM dbo.Orders t LEFT JOIN dbo.Customers c ON c.Id = t.CustomerId;
UPDATE t SET t.Archived = 1 FROM dbo.Orders t LEFT JOIN dbo.Customers c ON c.Id = t.CustomerId;
DELETE t FROM dbo.Customers c RIGHT JOIN dbo.Orders t ON t.CustomerId = c.Id;
DELETE t FROM dbo.Orders t FULL OUTER JOIN dbo.Customers c ON c.Id = t.CustomerId;
