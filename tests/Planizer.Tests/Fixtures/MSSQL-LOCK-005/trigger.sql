-- planizer-test: version=2019 edition=Enterprise
-- Online CREATE INDEX on 2019+ can be RESUMABLE; without it a restart loses all progress.
-- expect: MSSQL-LOCK-005 severity=Info line=4
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);
