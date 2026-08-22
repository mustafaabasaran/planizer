-- A static procedure call is analyzable; not dynamic SQL.
-- expect-none: MSSQL-DYN-001
EXEC dbo.RebuildAllIndexes;
SELECT 1;
