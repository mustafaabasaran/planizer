-- planizer-test: version=2017 edition=Enterprise
-- Resumable CREATE INDEX needs 2019+; on 2017 there is nothing to suggest.
-- expect-none: MSSQL-LOCK-005
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);
