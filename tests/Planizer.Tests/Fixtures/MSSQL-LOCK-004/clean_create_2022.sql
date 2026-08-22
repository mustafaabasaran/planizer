-- planizer-test: version=2022 edition=Enterprise
-- CREATE INDEX accepts WAIT_AT_LOW_PRIORITY from SQL Server 2022 onward.
-- expect-none: MSSQL-LOCK-004
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)));
