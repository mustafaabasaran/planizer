-- planizer-test: edition=Enterprise
-- Same offline build on Enterprise: still blocks, but the fix can suggest ONLINE = ON.
-- expect: MSSQL-LOCK-002 severity=Warning line=4
CREATE INDEX IX_T_C ON dbo.T (C);
