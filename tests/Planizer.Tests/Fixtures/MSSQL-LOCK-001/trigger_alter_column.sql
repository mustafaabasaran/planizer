-- ALTER COLUMN resolves through the offline-certain classifier kinds instead of a blanket
-- inconclusive: dropping NOT NULL is metadata-only (brief Sch-M, Info); NULL -> NOT NULL
-- validates every row under Sch-M (Warning). Only a bare type respecification stays
-- inconclusive (the current type is unknown offline).
-- expect: MSSQL-LOCK-001 severity=Info line=7
-- expect: MSSQL-LOCK-001 severity=Warning line=8
ALTER TABLE dbo.T ALTER COLUMN C int NULL;
ALTER TABLE dbo.T ALTER COLUMN D int NOT NULL;
