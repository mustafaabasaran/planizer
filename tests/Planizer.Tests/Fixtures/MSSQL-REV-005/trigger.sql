-- expect: MSSQL-REV-005 severity=Warning line=2
SET IDENTITY_INSERT dbo.Orders ON;
INSERT INTO dbo.Orders (Id, Number) VALUES (1, 'A-1');
