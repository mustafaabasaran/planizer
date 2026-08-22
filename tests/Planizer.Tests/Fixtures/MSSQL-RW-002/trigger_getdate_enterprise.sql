-- planizer-test: edition=Enterprise
-- GETDATE() is evaluated once at statement start: a runtime constant regardless of
-- determinism, so the fast path holds and the ADD is metadata-only on Enterprise.
-- expect: MSSQL-RW-002 severity=Info line=5
ALTER TABLE dbo.Orders ADD CreatedAt datetime2 NOT NULL DEFAULT GETDATE();
