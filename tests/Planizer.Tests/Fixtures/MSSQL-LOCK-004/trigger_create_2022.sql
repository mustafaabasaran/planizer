-- planizer-test: version=2022 edition=Enterprise
-- CREATE INDEX accepts WAIT_AT_LOW_PRIORITY only from SQL Server 2022 onward,
-- so the suggestion is valid on this target.
-- expect: MSSQL-LOCK-004 severity=Info line=5
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);
