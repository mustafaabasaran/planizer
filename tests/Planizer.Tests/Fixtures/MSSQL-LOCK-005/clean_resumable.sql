-- planizer-test: version=2019 edition=Enterprise
-- RESUMABLE = ON is already specified; nothing to suggest.
-- expect-none: MSSQL-LOCK-005
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON, RESUMABLE = ON, MAX_DURATION = 60 MINUTES);
