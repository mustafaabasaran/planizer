-- Both an INSERT column list and an alias-qualified reference (o.Status, alias resolved to
-- dbo.Orders through the FROM clause) hit the new column in the same batch.
-- expect: MSSQL-BATCH-001 severity=Blocker line=6
-- expect: MSSQL-BATCH-001 severity=Blocker line=7
ALTER TABLE dbo.Orders ADD Status tinyint NULL;
INSERT INTO dbo.Orders (Id, Status) VALUES (1, 0);
UPDATE o SET o.Status = 1 FROM dbo.Orders o WHERE o.Id = 1;
