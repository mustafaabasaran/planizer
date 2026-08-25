-- planizer-test: version=2019 edition=Enterprise
-- The finding still stands inside an explicit transaction — progress is lost the same way —
-- but RESUMABLE = ON cannot be added here (error 574), so the fix says "move it out of the block".
-- expect: MSSQL-LOCK-005 severity=Info line=6
BEGIN TRANSACTION;
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);
COMMIT;
