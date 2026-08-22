-- The alias resolves to a temp table: session-scoped, no lock escalation on user tables and
-- nothing to restore.
-- expect-none: MSSQL-LOCK-009
-- expect-none: MSSQL-REV-001
CREATE TABLE #tmp (Id int NOT NULL);
DELETE T FROM #tmp T;
