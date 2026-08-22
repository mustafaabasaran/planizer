-- planizer-test: version=2016
-- CREATE OR ALTER arrived in SQL Server 2016 SP1; a bare 2016 target cannot prove the patch
-- level, so the rule warns instead of blocking.
-- expect: MSSQL-VER-001 severity=Warning line=8
-- expect: MSSQL-VER-001 severity=Warning line=10
-- expect: MSSQL-VER-001 severity=Warning line=12
-- expect: MSSQL-VER-001 severity=Warning line=14
CREATE OR ALTER PROCEDURE dbo.P AS SELECT 1;
GO
CREATE OR ALTER VIEW dbo.V AS SELECT 1 AS X;
GO
CREATE OR ALTER FUNCTION dbo.F() RETURNS int AS BEGIN RETURN 1; END;
GO
CREATE OR ALTER TRIGGER dbo.TR ON dbo.T AFTER INSERT AS SELECT 1;
