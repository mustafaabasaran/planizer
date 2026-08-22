-- Table variables and temp tables are session-scoped: no lock escalation on user tables.
-- A DELETE/UPDATE bounded by a JOIN is not an unfiltered full-table write either.
-- expect-none: MSSQL-LOCK-009
DECLARE @Ids TABLE (Id bigint NOT NULL);
DELETE FROM @Ids;
UPDATE @Ids SET Id = 0;
CREATE TABLE #Stage (Id int NOT NULL);
DELETE FROM #Stage;
DELETE d FROM dbo.ParameterGroupTranslation d INNER JOIN dbo.ParameterGroup m ON m.Id = d.MasterId;
UPDATE m SET m.WorkgroupId = p.WorkgroupId FROM dbo.Message m JOIN dbo.PosTransaction p ON p.Id = m.PosId;
