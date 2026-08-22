-- planizer-test: edition=Enterprise
-- No ONLINE option: this rule only concerns online index operations.
-- expect-none: MSSQL-LOCK-004
CREATE INDEX IX ON dbo.T (C);
