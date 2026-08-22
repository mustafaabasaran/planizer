-- SELECT … INTO creates the staging table just like CREATE TABLE would; dropping it at the end of
-- the same file is the staging pattern — a re-run recreates it first.
-- expect-none: MSSQL-IDEM-003
SELECT Id, Total INTO dbo.OrdersStaging FROM dbo.Orders WHERE Total > 0;
UPDATE o SET o.Flag = 1 FROM dbo.Orders o JOIN dbo.OrdersStaging s ON s.Id = o.Id;
DROP TABLE dbo.OrdersStaging;
