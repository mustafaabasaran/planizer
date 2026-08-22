-- DROP COLUMN / CONSTRAINT IF EXISTS (2016+) is idempotent by construction.
-- expect-none: MSSQL-IDEM-002
ALTER TABLE dbo.Orders DROP COLUMN IF EXISTS LegacyCode;
ALTER TABLE dbo.Orders DROP CONSTRAINT IF EXISTS FK_Orders_Customers;
