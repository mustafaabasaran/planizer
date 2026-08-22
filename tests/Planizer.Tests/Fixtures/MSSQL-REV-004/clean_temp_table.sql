-- TRUNCATE of a temp table: session-scoped, nothing to roll back, no FK can reference it.
-- expect-none: MSSQL-REV-004
CREATE TABLE #tmp (Id int NOT NULL);
TRUNCATE TABLE #tmp;
