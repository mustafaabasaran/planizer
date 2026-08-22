-- Temp tables are session-scoped: their Sch-M lock blocks nobody else.
-- expect-none: MSSQL-LOCK-001
CREATE TABLE #tmp (Id int NOT NULL);
ALTER TABLE #tmp ADD C int NULL;
TRUNCATE TABLE #tmp;
DROP TABLE #tmp;
