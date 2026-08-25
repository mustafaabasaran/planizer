-- planizer-test: version=2022 edition=Enterprise
-- An online NONCLUSTERED create takes a brief shared (S) lock to start and again to complete —
-- no blocking table Sch-M in any phase. CREATE INDEX accepts WAIT_AT_LOW_PRIORITY from 2022 on,
-- so the suggestion is valid on this target.
-- expect: MSSQL-LOCK-004 severity=Info line=6
CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);
