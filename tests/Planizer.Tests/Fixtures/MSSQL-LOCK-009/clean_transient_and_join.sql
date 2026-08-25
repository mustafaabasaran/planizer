-- Table variables and temp tables are session-scoped: no lock escalation on user tables — named
-- directly or through an alias, joined or not.
-- expect-none: MSSQL-LOCK-009
-- expect-none: MSSQL-REV-001
DECLARE @Ids TABLE (Id bigint NOT NULL);
DELETE FROM @Ids;
UPDATE @Ids SET Id = 0;
DELETE i FROM @Ids i CROSS JOIN dbo.ParameterGroup m;
CREATE TABLE #Stage (Id int NOT NULL);
DELETE FROM #Stage;
DELETE s FROM #Stage s LEFT JOIN dbo.ParameterGroup m ON m.Id = s.Id;
