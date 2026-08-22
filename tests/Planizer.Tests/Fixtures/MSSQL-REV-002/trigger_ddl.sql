-- planizer-test: rollback=true
-- DDL with no derivable inverse is reported per statement at Warning.
-- expect: MSSQL-REV-002 severity=Warning line=5
-- expect: MSSQL-REV-002 severity=Warning line=6
DROP INDEX IX_Orders_Status ON dbo.Orders;
ALTER INDEX IX_Orders_Customer ON dbo.Orders REBUILD WITH (FILLFACTOR = 80);
