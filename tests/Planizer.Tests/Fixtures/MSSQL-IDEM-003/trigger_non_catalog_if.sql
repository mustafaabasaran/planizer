-- An IF on a variable is not an existence guard.
-- expect: MSSQL-IDEM-003 severity=Warning line=5
DECLARE @cleanup bit = 1;
IF @cleanup = 1
    DROP TABLE dbo.Legacy;
