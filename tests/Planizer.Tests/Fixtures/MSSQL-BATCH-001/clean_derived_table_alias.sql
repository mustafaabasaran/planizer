-- A qualifier that resolves to a derived table, CTE, table variable or table-valued function in
-- the statement's own FROM clause names a column of THAT row source, not the new column.
-- expect-none: MSSQL-BATCH-001
ALTER TABLE dbo.Orders ADD Flag bit NULL;
SELECT x.Flag FROM dbo.Orders o CROSS APPLY (SELECT o.Id AS Flag) x;
WITH c AS (SELECT o.Id AS Flag FROM dbo.Orders o) SELECT c.Flag FROM c;
DECLARE @t TABLE (Flag bit NULL);
SELECT v.Flag FROM dbo.Orders o JOIN @t v ON v.Flag = 1;
SELECT f.Flag FROM dbo.Orders o CROSS APPLY dbo.fn_Flags(o.Id) f;
