-- A plain index and a non-persisted computed column do not care about these SET options,
-- even with QUOTED_IDENTIFIER explicitly OFF.
-- expect-none: MSSQL-SET-001
SET QUOTED_IDENTIFIER OFF;
CREATE INDEX IX_Orders_Customer ON dbo.Orders (CustomerId);
ALTER TABLE dbo.Orders ADD TotalWithTax AS (Total * 1.2);
CREATE TABLE dbo.T (Id int NOT NULL, A int NULL, B AS (A + 1));
