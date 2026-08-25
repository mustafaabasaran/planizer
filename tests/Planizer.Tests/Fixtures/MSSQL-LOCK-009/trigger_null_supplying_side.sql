-- The target sits on the null-supplying side of an outer join: filtered exactly like an inner
-- join, so whether every row matches is a data question — Info + inconclusive, never Critical.
-- expect: MSSQL-LOCK-009 severity=Info
-- expect-none: MSSQL-REV-001
DELETE d FROM dbo.ParameterGroup m LEFT JOIN dbo.ParameterGroupTranslation d ON d.MasterId = m.Id;
UPDATE d SET d.Name = m.Name FROM dbo.ParameterGroup m LEFT JOIN dbo.ParameterGroupTranslation d ON d.MasterId = m.Id;
DELETE d FROM dbo.ParameterGroupTranslation d RIGHT JOIN dbo.ParameterGroup m ON m.Id = d.MasterId;
