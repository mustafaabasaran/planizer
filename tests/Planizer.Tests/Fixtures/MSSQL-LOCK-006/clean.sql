-- REORGANIZE is always online; nothing for this rule to flag.
-- expect-none: MSSQL-LOCK-006
ALTER INDEX IX ON dbo.T REORGANIZE;
