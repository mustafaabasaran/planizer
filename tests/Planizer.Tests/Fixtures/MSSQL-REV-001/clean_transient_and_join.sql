-- A WHERE-less DELETE on a table variable / temp table loses no persistent data. An outer join
-- with the target on its null-supplying side restricts the delete to the matched rows. An INNER
-- JOIN may or may not restrict it — Critical on a guess would be wrong, so this rule stays silent
-- and MSSQL-LOCK-009 reports the uncertainty as Info instead.
-- expect-none: MSSQL-REV-001
DECLARE @Ids TABLE (Id bigint NOT NULL);
DELETE FROM @Ids;
DELETE i FROM @Ids i CROSS JOIN dbo.ParameterGroup m;
CREATE TABLE #Stage (Id int NOT NULL);
DELETE FROM #Stage;
DELETE d FROM dbo.ParameterGroup m LEFT JOIN dbo.ParameterGroupTranslation d ON d.MasterId = m.Id;
DELETE d FROM dbo.ParameterGroupTranslation d INNER JOIN dbo.ParameterGroup m ON m.Id = d.MasterId;
