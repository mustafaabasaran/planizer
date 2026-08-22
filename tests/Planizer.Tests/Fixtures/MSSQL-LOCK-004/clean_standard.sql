-- planizer-test: edition=Standard
-- ONLINE = ON cannot run on Standard at all (MSSQL-LOCK-003 blocks the statement);
-- WAIT_AT_LOW_PRIORITY tuning advice would contradict that Blocker.
-- expect-none: MSSQL-LOCK-004
-- expect: MSSQL-LOCK-003 severity=Blocker line=6
ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON);
