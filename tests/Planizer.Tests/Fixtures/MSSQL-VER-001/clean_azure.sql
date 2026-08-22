-- planizer-test: version=azure edition=Azure
-- Azure SQL Database is always current: no version gate applies.
-- expect-none: MSSQL-VER-001
SELECT DATE_BUCKET(day, 1, CreatedAt), STRING_AGG(Name, ',') FROM dbo.T;
DROP TABLE IF EXISTS dbo.Old;
