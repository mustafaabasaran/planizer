-- planizer-test: version=2019 edition=Standard
-- ONLINE = ON cannot run on Standard at all (MSSQL-LOCK-003 blocks the statement);
-- RESUMABLE tuning advice would contradict that Blocker.
-- expect-none: MSSQL-LOCK-005
-- expect: MSSQL-LOCK-003 severity=Blocker line=6
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);
