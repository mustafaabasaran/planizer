-- planizer-test: version=2017
-- From SQL Server 2017 on CREATE OR ALTER is unconditionally available.
-- expect-none: MSSQL-VER-001
CREATE OR ALTER PROCEDURE dbo.P AS SELECT 1;
