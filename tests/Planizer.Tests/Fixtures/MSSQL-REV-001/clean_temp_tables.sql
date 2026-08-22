-- Dropping, truncating or clearing a temp table loses no persistent data.
-- expect-none: MSSQL-REV-001
CREATE TABLE #tmp (Id int NOT NULL);
DELETE FROM #tmp;
TRUNCATE TABLE #tmp;
DROP TABLE #tmp;
