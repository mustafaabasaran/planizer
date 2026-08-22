-- planizer-test: version=2014
-- Built-in functions added in SQL Server 2016: the 2014 grammar parses the calls (a function
-- name is just an identifier), so only the feature catalog can tell they will fail with
-- "not a recognized built-in function name" on the target.
-- expect: MSSQL-VER-001 severity=Blocker line=16
-- expect: MSSQL-VER-001 severity=Blocker line=17
-- expect: MSSQL-VER-001 severity=Blocker line=18
-- expect: MSSQL-VER-001 severity=Blocker line=19
-- expect: MSSQL-VER-001 severity=Blocker line=20
-- expect: MSSQL-VER-001 severity=Blocker line=21
-- expect: MSSQL-VER-001 severity=Blocker line=22
-- expect: MSSQL-VER-001 severity=Blocker line=23
-- expect: MSSQL-VER-001 severity=Blocker line=24
-- expect: MSSQL-VER-001 severity=Blocker line=25
-- expect: MSSQL-VER-001 severity=Blocker line=26
SELECT value FROM STRING_SPLIT('a,b', ',');
SELECT * FROM OPENJSON(@json);
SELECT JSON_VALUE(@json, '$.a');
SELECT JSON_QUERY(@json, '$.b');
SELECT JSON_MODIFY(@json, '$.a', 1);
SELECT ISJSON(@json);
SELECT COMPRESS(@blob);
SELECT DECOMPRESS(@blob);
SELECT DATEDIFF_BIG(millisecond, @a, @b);
SELECT STRING_ESCAPE(@text, 'json');
SELECT SESSION_CONTEXT(N'tenant');
