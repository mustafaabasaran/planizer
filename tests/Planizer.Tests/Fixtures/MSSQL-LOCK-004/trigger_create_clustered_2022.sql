-- planizer-test: version=2022 edition=Enterprise
-- An online CLUSTERED create takes Sch-M in its final phase (unlike a nonclustered create,
-- which ends on a second shared (S) lock); the message must say so.
-- expect: MSSQL-LOCK-004 severity=Info line=5
CREATE CLUSTERED INDEX CX ON dbo.T (Id) WITH (ONLINE = ON);
