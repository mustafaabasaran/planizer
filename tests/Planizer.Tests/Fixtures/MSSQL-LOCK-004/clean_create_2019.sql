-- planizer-test: version=2019 edition=Enterprise
-- CREATE INDEX does not accept WAIT_AT_LOW_PRIORITY before SQL Server 2022:
-- suggesting it here would recommend syntax the target server rejects.
-- expect-none: MSSQL-LOCK-004
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);
