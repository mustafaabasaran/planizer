-- A join that cannot drop rows of the target is not a filter: every row is deleted, and no
-- rollback script brings the rows back.
-- expect: MSSQL-REV-001 severity=Critical line=9
-- expect: MSSQL-REV-001 severity=Critical line=10
-- expect: MSSQL-REV-001 severity=Critical line=11
-- expect: MSSQL-REV-001 severity=Critical line=12
-- expect: MSSQL-REV-001 severity=Critical line=13
-- expect: MSSQL-REV-001 severity=Critical line=14
DELETE t FROM dbo.Orders t LEFT JOIN dbo.Customers c ON c.Id = t.CustomerId;
DELETE t FROM dbo.Customers c RIGHT JOIN dbo.Orders t ON t.CustomerId = c.Id;
DELETE t FROM dbo.Orders t FULL OUTER JOIN dbo.Customers c ON c.Id = t.CustomerId;
DELETE t FROM dbo.Orders t CROSS JOIN dbo.Customers c;
DELETE t FROM dbo.Orders t, dbo.Customers c;
DELETE t FROM dbo.Orders t OUTER APPLY (SELECT TOP (1) c.Id AS Id FROM dbo.Customers c WHERE c.Id = t.CustomerId) x;
