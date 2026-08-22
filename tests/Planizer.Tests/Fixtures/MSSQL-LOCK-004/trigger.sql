-- planizer-test: edition=Enterprise
-- ONLINE = ON still takes brief Sch-M locks at start/end; without WAIT_AT_LOW_PRIORITY they can
-- convoy. ALTER INDEX ... REBUILD accepts the option from SQL Server 2014 (default target 2019).
-- expect: MSSQL-LOCK-004 severity=Info line=5
ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON);
