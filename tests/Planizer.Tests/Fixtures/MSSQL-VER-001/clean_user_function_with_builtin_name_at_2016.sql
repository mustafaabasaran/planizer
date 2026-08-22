-- planizer-test: version=2016
-- A schema-qualified call is a user-defined function, whatever its name; only bare built-in
-- calls are version-gated. ALTER INDEX REBUILD has accepted WAIT_AT_LOW_PRIORITY since 2014.
-- expect-none: MSSQL-VER-001
SELECT dbo.STRING_AGG(Name, ',') FROM dbo.T;
SELECT util.TRIM(Name) FROM dbo.T;
SELECT LTRIM(Name) FROM dbo.T;
ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)));
