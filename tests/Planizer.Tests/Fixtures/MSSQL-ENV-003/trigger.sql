-- Two whole-table operations (index build, clustered index rewrite) and not a single PRINT or
-- RAISERROR ... WITH NOWAIT: the run is silent until it ends. One Info per file.
-- expect: MSSQL-ENV-003 severity=Info line=4
CREATE INDEX IX_Orders_Customer ON dbo.Orders (CustomerId);
CREATE CLUSTERED INDEX CX_Orders ON dbo.Orders (Id);
