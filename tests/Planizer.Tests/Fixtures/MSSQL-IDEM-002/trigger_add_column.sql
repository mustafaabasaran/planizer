-- Re-running an unguarded ADD fails: "Column names in each table must be unique" (2705).
-- expect: MSSQL-IDEM-002 severity=Warning line=3
ALTER TABLE dbo.Orders ADD Status tinyint NOT NULL CONSTRAINT DF_Orders_Status DEFAULT 0;
