-- planizer-test: edition=Standard
-- On Standard a NOT NULL column with a constant default rewrites every row: a long, silent run.
-- expect: MSSQL-ENV-003 severity=Info line=4
ALTER TABLE dbo.Orders ADD IsArchived bit NOT NULL CONSTRAINT DF_Orders_IsArchived DEFAULT 0;
