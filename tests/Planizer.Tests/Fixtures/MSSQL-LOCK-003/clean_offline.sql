-- planizer-test: edition=Standard
-- No ONLINE option at all: nothing for this rule to flag on Standard.
-- expect-none: MSSQL-LOCK-003
CREATE INDEX IX ON dbo.T (C);
