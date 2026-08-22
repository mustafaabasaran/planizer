-- Table variables die at GO as well (error 1087, "Must declare the table variable").
-- expect: MSSQL-BATCH-002 severity=Blocker line=6
DECLARE @ids TABLE (Id int NOT NULL);
INSERT INTO @ids (Id) SELECT Id FROM dbo.Orders WHERE Status = 9;
GO
DELETE FROM dbo.OrderLines WHERE OrderId IN (SELECT Id FROM @ids);
