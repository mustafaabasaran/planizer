-- planizer-test: edition=Standard
-- expect: MSSQL-RW-002 severity=Critical line=3
ALTER TABLE dbo.Orders ADD Status int NOT NULL DEFAULT 0;
