-- planizer-test: edition=Enterprise
-- Online rebuild: this rule is about offline rebuilds only.
-- expect-none: MSSQL-LOCK-006
ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON);
