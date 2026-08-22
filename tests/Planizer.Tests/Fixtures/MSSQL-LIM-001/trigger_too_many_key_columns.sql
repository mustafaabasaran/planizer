-- 33 key columns: the key column limit is 32 since SQL Server 2016 (16 before). The count
-- alone is conclusive, so no CREATE TABLE is needed in the file. Error 1904 at CREATE time.
-- expect: MSSQL-LIM-001 severity=Blocker line=4
CREATE INDEX IX_Wide ON dbo.Wide (C01, C02, C03, C04, C05, C06, C07, C08, C09, C10, C11, C12, C13, C14, C15, C16, C17, C18, C19, C20, C21, C22, C23, C24, C25, C26, C27, C28, C29, C30, C31, C32, C33);
