-- planizer-test: edition=Enterprise
-- expect: MSSQL-RW-002 severity=Info line=3
ALTER TABLE dbo.Orders ADD Status int NOT NULL DEFAULT 0;
