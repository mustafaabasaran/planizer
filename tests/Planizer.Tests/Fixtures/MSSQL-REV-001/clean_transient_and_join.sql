-- WHERE-less DELETE on a table variable / temp table loses no persistent data, and a
-- JOIN-bounded DELETE is not a full-table wipe.
-- expect-none: MSSQL-REV-001
DECLARE @Ids TABLE (Id bigint NOT NULL);
DELETE FROM @Ids;
CREATE TABLE #Stage (Id int NOT NULL);
DELETE FROM #Stage;
DELETE d FROM dbo.ParameterGroupTranslation d INNER JOIN dbo.ParameterGroup m ON m.Id = d.MasterId;
