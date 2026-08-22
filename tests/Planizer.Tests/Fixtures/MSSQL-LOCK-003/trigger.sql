-- planizer-test: edition=Standard
-- ONLINE = ON is Enterprise-only: this statement fails outright with error 1712.
-- expect: MSSQL-LOCK-003 severity=Blocker line=4
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);
