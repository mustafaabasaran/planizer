-- Offline REBUILD on the default Standard edition holds Sch-M for the whole rebuild.
-- expect: MSSQL-LOCK-006 severity=Warning line=3
ALTER INDEX IX ON dbo.T REBUILD;
