-- planizer-test: edition=Express
-- ALTER INDEX ... REBUILD WITH (ONLINE = ON) fails on Express as well.
-- expect: MSSQL-LOCK-003 severity=Blocker line=4
ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON);
