-- planizer-test: edition=Enterprise
-- WAIT_AT_LOW_PRIORITY is specified: the brief start/finish locks wait politely instead of convoying.
-- expect-none: MSSQL-LOCK-004
ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)));
