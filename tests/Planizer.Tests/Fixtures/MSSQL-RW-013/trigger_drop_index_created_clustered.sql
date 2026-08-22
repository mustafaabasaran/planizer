-- The dropped index was created CLUSTERED earlier in this file: the drop is a certain full
-- rewrite (table back to a heap), not an inconclusive warning.
-- expect: MSSQL-RW-013 severity=Critical line=5
-- expect: MSSQL-RW-013 severity=Critical line=7
CREATE CLUSTERED INDEX IX_Heap_Id ON dbo.Heap (Id);
GO
DROP INDEX IX_Heap_Id ON dbo.Heap;
