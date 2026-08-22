-- planizer-test: version=2017 edition=Enterprise
-- SQL Server 2019: APPROX_COUNT_DISTINCT and resumable online CREATE INDEX.
-- expect: MSSQL-VER-001 severity=Blocker line=6
-- expect: MSSQL-VER-001 severity=Blocker line=7
-- expect-none: MSSQL-PARSE-001
SELECT APPROX_COUNT_DISTINCT(Id) FROM dbo.T;
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON, RESUMABLE = ON);
