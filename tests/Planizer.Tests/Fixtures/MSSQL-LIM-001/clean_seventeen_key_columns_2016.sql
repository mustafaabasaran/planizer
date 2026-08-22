-- planizer-test: version=2016
-- 17 key columns are fine from SQL Server 2016 on (limit 32).
-- expect-none: MSSQL-LIM-001
CREATE INDEX IX_Wide ON dbo.Wide (C01, C02, C03, C04, C05, C06, C07, C08, C09, C10, C11, C12, C13, C14, C15, C16, C17);
