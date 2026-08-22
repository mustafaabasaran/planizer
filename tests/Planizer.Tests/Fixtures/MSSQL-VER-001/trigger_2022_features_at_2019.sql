-- planizer-test: version=2019 edition=Enterprise
-- SQL Server 2022: new functions, new argument forms of old functions (LTRIM/RTRIM characters,
-- STRING_SPLIT enable_ordinal), WAIT_AT_LOW_PRIORITY on CREATE INDEX, and grammar-level
-- syntax (JSON_OBJECT, TRIM LEADING, IS DISTINCT FROM, WINDOW, LEDGER).
-- expect: MSSQL-VER-001 severity=Blocker line=29
-- expect: MSSQL-VER-001 severity=Blocker line=30
-- expect: MSSQL-VER-001 severity=Blocker line=31
-- expect: MSSQL-VER-001 severity=Blocker line=32
-- expect: MSSQL-VER-001 severity=Blocker line=33
-- expect: MSSQL-VER-001 severity=Blocker line=34
-- expect: MSSQL-VER-001 severity=Blocker line=35
-- expect: MSSQL-VER-001 severity=Blocker line=36
-- expect: MSSQL-VER-001 severity=Blocker line=37
-- expect: MSSQL-VER-001 severity=Blocker line=38
-- expect: MSSQL-VER-001 severity=Blocker line=39
-- expect: MSSQL-VER-001 severity=Blocker line=40
-- expect: MSSQL-VER-001 severity=Blocker line=41
-- expect: MSSQL-VER-001 severity=Blocker line=42
-- expect: MSSQL-VER-001 severity=Blocker line=43
-- expect: MSSQL-VER-001 severity=Blocker line=44
-- expect: MSSQL-VER-001 severity=Blocker line=45
-- expect: MSSQL-VER-001 severity=Blocker line=46
-- expect: MSSQL-VER-001 severity=Blocker line=47
-- expect: MSSQL-VER-001 severity=Blocker line=48
-- expect: MSSQL-VER-001 severity=Blocker line=49
-- expect: MSSQL-VER-001 severity=Blocker line=50
-- expect: MSSQL-VER-001 severity=Blocker line=51
-- expect-none: MSSQL-PARSE-001
SELECT GREATEST(A, B) FROM dbo.T;
SELECT LEAST(A, B) FROM dbo.T;
SELECT DATE_BUCKET(day, 1, CreatedAt) FROM dbo.T;
SELECT DATETRUNC(day, CreatedAt) FROM dbo.T;
SELECT value FROM GENERATE_SERIES(1, 10);
SELECT JSON_PATH_EXISTS(@json, '$.a');
SELECT JSON_OBJECT('a': 1);
SELECT JSON_ARRAY(1, 2);
SELECT APPROX_PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY Id) FROM dbo.T;
SELECT APPROX_PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY Id) FROM dbo.T;
SELECT BIT_COUNT(Flags) FROM dbo.T;
SELECT GET_BIT(Flags, 1) FROM dbo.T;
SELECT SET_BIT(Flags, 1) FROM dbo.T;
SELECT LEFT_SHIFT(Flags, 1) FROM dbo.T;
SELECT RIGHT_SHIFT(Flags, 1) FROM dbo.T;
SELECT LTRIM(Name, 'x') FROM dbo.T;
SELECT RTRIM(Name, 'x') FROM dbo.T;
SELECT value FROM STRING_SPLIT('a,b', ',', 1);
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)));
SELECT TRIM(LEADING 'x' FROM Name) FROM dbo.T;
SELECT 1 FROM dbo.T WHERE A IS DISTINCT FROM B;
SELECT SUM(A) OVER w FROM dbo.T WINDOW w AS (PARTITION BY B);
CREATE TABLE dbo.L (Id int NOT NULL) WITH (LEDGER = ON);
