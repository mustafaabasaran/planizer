-- The table itself is created in this batch, so name resolution for statements that use it is
-- deferred to execution time and the added column is visible by then.
-- expect-none: MSSQL-BATCH-001
CREATE TABLE dbo.OrderStaging (Id int NOT NULL PRIMARY KEY);
ALTER TABLE dbo.OrderStaging ADD Status tinyint NULL;
INSERT INTO dbo.OrderStaging (Id, Status) VALUES (1, 0);
