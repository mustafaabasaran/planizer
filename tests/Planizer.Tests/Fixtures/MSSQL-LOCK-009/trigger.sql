-- UPDATE/DELETE with no WHERE and no TOP: after ~5000 row locks, lock escalation
-- converts the operation into a table lock.
-- expect: MSSQL-LOCK-009 severity=Warning line=5
-- expect: MSSQL-LOCK-009 severity=Warning line=6
DELETE FROM dbo.Big;
UPDATE dbo.Big SET Archived = 1;
