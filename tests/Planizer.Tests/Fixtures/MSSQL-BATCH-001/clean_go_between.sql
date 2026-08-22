-- GO ends the batch: the UPDATE is compiled after the ALTER TABLE has run.
-- expect-none: MSSQL-BATCH-001
ALTER TABLE dbo.Orders ADD Status tinyint NULL;
GO
UPDATE dbo.Orders SET Status = 3 WHERE ShippedDate IS NOT NULL;
