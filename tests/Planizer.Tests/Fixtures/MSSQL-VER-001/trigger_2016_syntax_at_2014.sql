-- planizer-test: version=2014
-- Syntax the 2014 grammar rejects but the 2016 grammar accepts: reported as VER-001 (not
-- PARSE-001), and the statements are still analysed — LOCK-001 sees the DROP TABLE.
-- expect: MSSQL-VER-001 severity=Blocker line=10
-- expect: MSSQL-VER-001 severity=Blocker line=11
-- expect: MSSQL-VER-001 severity=Blocker line=12
-- expect: MSSQL-VER-001 severity=Blocker line=14
-- expect: MSSQL-LOCK-001 line=10
-- expect-none: MSSQL-PARSE-001
DROP TABLE IF EXISTS dbo.Old;
SELECT SYSDATETIME() AT TIME ZONE 'UTC';
SELECT a FROM OPENJSON(@json) WITH (a int '$.a');
GO
CREATE OR ALTER PROCEDURE dbo.P AS SELECT 1;
