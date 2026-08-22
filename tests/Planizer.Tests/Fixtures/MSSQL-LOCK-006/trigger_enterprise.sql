-- planizer-test: edition=Enterprise
-- Offline REBUILD on Enterprise: the fix can suggest ONLINE = ON.
-- expect: MSSQL-LOCK-006 severity=Warning line=4
ALTER INDEX ALL ON dbo.T REBUILD;
