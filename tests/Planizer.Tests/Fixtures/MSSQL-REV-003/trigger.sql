-- expect: MSSQL-REV-003 severity=Critical line=3
-- expect: MSSQL-REV-003 severity=Warning line=4
EXEC sp_rename 'dbo.Orders.CustName', 'CustomerName', 'COLUMN';
EXEC sp_rename 'dbo.OldOrders', 'ArchivedOrders';
