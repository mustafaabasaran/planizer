-- Temp tables and table variables are session-scoped; nothing persists to the next run.
-- expect-none: MSSQL-IDEM-002
CREATE TABLE #work (Id int NOT NULL);
ALTER TABLE #work ADD Flag bit NULL;
ALTER TABLE #work DROP COLUMN Flag;
