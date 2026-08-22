-- ANSI_NULLS OFF breaks PERSISTED computed columns the same way (error 1934); both the
-- ALTER TABLE ADD form and the CREATE TABLE form are checked.
-- expect: MSSQL-SET-001 severity=Blocker line=6
-- expect: MSSQL-SET-001 severity=Blocker line=7
SET ANSI_NULLS OFF;
ALTER TABLE dbo.Orders ADD TotalWithTax AS (Total * 1.2) PERSISTED;
CREATE TABLE dbo.OrderTotals (Id int NOT NULL, Net money NOT NULL, Gross AS (Net * 1.2) PERSISTED);
