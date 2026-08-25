-- An INNER JOIN or a CROSS APPLY drops target rows only when the other side can come back empty;
-- offline that cannot be decided, so the rule reports Info + inconclusive instead of staying
-- silent. MSSQL-REV-001 keeps quiet here — Critical on a guess would be wrong.
-- expect: MSSQL-LOCK-009 severity=Info line=7
-- expect: MSSQL-LOCK-009 severity=Info line=8
-- expect-none: MSSQL-REV-001
DELETE d FROM dbo.ParameterGroupTranslation d INNER JOIN dbo.ParameterGroup m ON m.Id = d.MasterId;
DELETE t FROM dbo.Orders t CROSS APPLY (SELECT TOP (1) c.Id AS Id FROM dbo.Customers c WHERE c.Id = t.CustomerId) x;
