-- planizer-test: version=2022 edition=Enterprise
-- Every catalogued feature is available on SQL Server 2022: nothing to report.
-- expect-none: MSSQL-VER-001
-- expect-none: MSSQL-PARSE-001
SELECT value FROM STRING_SPLIT('a,b', ',', 1);
SELECT JSON_VALUE(@json, '$.a'), JSON_OBJECT('a': 1), GREATEST(1, 2), STRING_AGG(Name, ',') FROM dbo.T;
SELECT SYSDATETIME() AT TIME ZONE 'UTC', TRIM(LEADING 'x' FROM Name), LTRIM(Name, 'x') FROM dbo.T;
DROP TABLE IF EXISTS dbo.Old;
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)), RESUMABLE = ON);
ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON, RESUMABLE = ON);
GO
CREATE OR ALTER PROCEDURE dbo.P AS SELECT 1;
