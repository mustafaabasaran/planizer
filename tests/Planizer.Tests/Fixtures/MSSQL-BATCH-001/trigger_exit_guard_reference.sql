-- The exit guard (IF … IS NOT NULL RETURN) makes the script re-runnable, but the batch is still
-- compiled as a whole on a fresh database: the UPDATE on line 7 fails with error 207 there and
-- only compiles where an earlier run already added the column — the message says so.
-- expect: MSSQL-BATCH-001 severity=Blocker line=7
IF COL_LENGTH('dbo.Orders', 'Status') IS NOT NULL RETURN;
ALTER TABLE dbo.Orders ADD Status tinyint NULL;
UPDATE dbo.Orders SET Status = 0 WHERE Status IS NULL;
