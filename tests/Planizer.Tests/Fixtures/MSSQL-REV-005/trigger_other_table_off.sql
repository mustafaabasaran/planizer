-- The OFF is for a different table; dbo.Orders is still left ON.
-- expect: MSSQL-REV-005 severity=Warning line=3
SET IDENTITY_INSERT dbo.Orders ON;
SET IDENTITY_INSERT dbo.Products OFF;
