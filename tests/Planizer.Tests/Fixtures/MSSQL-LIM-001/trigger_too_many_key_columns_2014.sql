-- planizer-test: version=2014
-- 17 key columns exceed the pre-2016 limit of 16 (a PRIMARY KEY counts like any index key).
-- expect: MSSQL-LIM-001 severity=Blocker line=4
ALTER TABLE dbo.Wide ADD CONSTRAINT PK_Wide PRIMARY KEY (C01, C02, C03, C04, C05, C06, C07, C08, C09, C10, C11, C12, C13, C14, C15, C16, C17);
