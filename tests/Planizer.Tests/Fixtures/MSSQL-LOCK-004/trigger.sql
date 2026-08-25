-- planizer-test: edition=Enterprise
-- ONLINE = ON is not lock-free: a rebuild starts on a brief shared (S) lock and finishes on a
-- Sch-M lock. Without WAIT_AT_LOW_PRIORITY those queue at normal priority and can convoy.
-- ALTER INDEX ... REBUILD accepts the option from SQL Server 2014 (default target 2019).
-- expect: MSSQL-LOCK-004 severity=Info line=6
ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON);
