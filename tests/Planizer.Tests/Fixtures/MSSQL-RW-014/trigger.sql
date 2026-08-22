-- expect: MSSQL-RW-014 severity=Warning line=3
-- expect: MSSQL-RW-014 severity=Warning line=5
ALTER TABLE dbo.Orders ADD CONSTRAINT FK_Orders_Customers
    FOREIGN KEY (CustomerId) REFERENCES dbo.Customers (Id);
ALTER TABLE dbo.Orders ADD CONSTRAINT CK_Orders_Quantity CHECK (Quantity > 0);
