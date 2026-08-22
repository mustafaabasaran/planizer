-- A default constraint for an existing column validates nothing; no scan.
-- expect-none: MSSQL-RW-014
ALTER TABLE dbo.Orders ADD CONSTRAINT DF_Orders_Status DEFAULT (0) FOR Status;
