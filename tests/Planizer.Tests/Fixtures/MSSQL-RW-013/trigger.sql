-- expect: MSSQL-RW-013 severity=Critical line=2
CREATE CLUSTERED INDEX IX_HeapTable_Id ON dbo.HeapTable (Id);
