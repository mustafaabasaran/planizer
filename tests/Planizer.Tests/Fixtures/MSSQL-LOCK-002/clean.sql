-- planizer-test: edition=Enterprise
-- ONLINE = ON: the build itself does not hold the blocking lock.
-- expect-none: MSSQL-LOCK-002
CREATE INDEX IX_T_C ON dbo.T (C) WITH (ONLINE = ON);
