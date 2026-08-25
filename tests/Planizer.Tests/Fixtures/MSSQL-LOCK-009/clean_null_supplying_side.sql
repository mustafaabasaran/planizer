-- The target sits on the null-supplying side of an outer join: its rows without a match are
-- dropped from the result, so the write is restricted to the matched ones.
-- expect-none: MSSQL-LOCK-009
-- expect-none: MSSQL-REV-001
DELETE d FROM dbo.ParameterGroup m LEFT JOIN dbo.ParameterGroupTranslation d ON d.MasterId = m.Id;
UPDATE d SET d.Name = m.Name FROM dbo.ParameterGroup m LEFT JOIN dbo.ParameterGroupTranslation d ON d.MasterId = m.Id;
DELETE d FROM dbo.ParameterGroupTranslation d RIGHT JOIN dbo.ParameterGroup m ON m.Id = d.MasterId;
