-- Ordinary procedure calls are not renames.
-- expect-none: MSSQL-REV-003
EXEC dbo.RecalculateTotals 'dbo.Orders', 'full';
SELECT 1;
