-- Temp tables are session-scoped; dropping one never hits a previous run's state.
-- expect-none: MSSQL-IDEM-003
CREATE TABLE #work (Id int NOT NULL);
DROP TABLE #work;
