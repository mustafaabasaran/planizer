-- Progress is announced (RAISERROR ... WITH NOWAIT and PRINT both count): quiet.
-- expect-none: MSSQL-ENV-003
RAISERROR('step 1: index Orders', 0, 1) WITH NOWAIT;
CREATE INDEX IX_Orders_Customer ON dbo.Orders (CustomerId);
PRINT 'step 2: cluster Orders';
CREATE CLUSTERED INDEX CX_Orders ON dbo.Orders (Id);
