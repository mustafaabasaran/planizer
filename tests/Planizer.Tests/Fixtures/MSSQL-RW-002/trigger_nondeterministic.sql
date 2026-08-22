-- planizer-test: edition=Enterprise
-- A per-row default (NEWID is evaluated for every row) breaks the metadata-only
-- fast path even on Enterprise.
-- expect: MSSQL-RW-002 severity=Critical line=5
ALTER TABLE dbo.Orders ADD RowGuid uniqueidentifier NOT NULL DEFAULT NEWID();
