-- A WHERE clause (or TOP) keeps the touched row count bounded.
-- expect-none: MSSQL-LOCK-009
DELETE FROM dbo.Big WHERE Id < 100;
UPDATE dbo.Big SET Archived = 1 WHERE CreatedAt < '2020-01-01';
