-- planizer-test: version=2017 edition=Enterprise
-- Online REBUILD is resumable from 2017 onward.
-- expect: MSSQL-LOCK-005 severity=Info line=4
ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON);
