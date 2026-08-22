-- TOP bounds the batch size even without a WHERE clause.
-- expect-none: MSSQL-LOCK-009
DELETE TOP (4000) FROM dbo.Big;
