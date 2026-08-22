-- An IF on a variable is not an existence guard.
-- expect: MSSQL-IDEM-002 severity=Warning line=5
DECLARE @apply bit = 1;
IF @apply = 1
    ALTER TABLE dbo.Orders ADD Status tinyint NULL;
