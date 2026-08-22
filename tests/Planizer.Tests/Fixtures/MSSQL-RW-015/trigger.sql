-- expect: MSSQL-RW-015 severity=Warning line=3
-- expect: MSSQL-RW-015 severity=Warning line=4
ALTER TABLE dbo.Orders ADD CONSTRAINT PK_Orders PRIMARY KEY (Id);
ALTER TABLE dbo.Orders ADD CONSTRAINT UQ_Orders_Code UNIQUE (Code);
