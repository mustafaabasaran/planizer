-- expect-none: MSSQL-RW-001
-- NOT NULL + DEFAULT is RW-002 territory; a computed column is not a nullable data column.
ALTER TABLE dbo.Orders ADD Status int NOT NULL DEFAULT 0;
ALTER TABLE dbo.Orders ADD Total AS (Qty * Price);
