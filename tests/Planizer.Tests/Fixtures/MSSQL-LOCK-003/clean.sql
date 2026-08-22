-- planizer-test: edition=Enterprise
-- Enterprise supports online index operations; nothing to block here.
-- expect-none: MSSQL-LOCK-003
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);
