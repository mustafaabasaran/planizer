-- The batch is compiled as a whole before the ALTER TABLE runs: the UPDATE on line 6 references
-- a column that does not exist at compile time (error 207) even though the ADD precedes it.
-- expect: MSSQL-BATCH-001 severity=Blocker line=6
ALTER TABLE dbo.Orders ADD Status tinyint NOT NULL CONSTRAINT DF_Orders_Status DEFAULT (0);

UPDATE dbo.Orders SET Status = 3 WHERE ShippedDate IS NOT NULL;
