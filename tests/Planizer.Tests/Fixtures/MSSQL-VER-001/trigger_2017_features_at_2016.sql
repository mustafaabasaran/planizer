-- planizer-test: version=2016
-- SQL Server 2017: new string functions and resumable ALTER INDEX REBUILD. RESUMABLE is a
-- grammar-level feature (2016 grammar rejects it) but the catalog names it precisely.
-- expect: MSSQL-VER-001 severity=Blocker line=10
-- expect: MSSQL-VER-001 severity=Blocker line=11
-- expect: MSSQL-VER-001 severity=Blocker line=12
-- expect: MSSQL-VER-001 severity=Blocker line=13
-- expect: MSSQL-VER-001 severity=Blocker line=14
-- expect-none: MSSQL-PARSE-001
SELECT STRING_AGG(Name, ',') FROM dbo.T;
SELECT TRIM(Name) FROM dbo.T;
SELECT CONCAT_WS('-', A, B) FROM dbo.T;
SELECT TRANSLATE(Name, 'abc', 'xyz') FROM dbo.T;
ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON, RESUMABLE = ON);
