-- planizer-test: edition=Enterprise
-- The same ADD is metadata-only on Enterprise: nothing runs long, so no message is needed.
-- expect-none: MSSQL-ENV-003
ALTER TABLE dbo.Orders ADD IsArchived bit NOT NULL CONSTRAINT DF_Orders_IsArchived DEFAULT 0;
