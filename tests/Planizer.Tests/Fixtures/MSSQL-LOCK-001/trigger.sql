-- expect: MSSQL-LOCK-001 severity=Warning line=3
-- expect: MSSQL-LOCK-001 severity=Warning line=4
ALTER TABLE dbo.T ADD C int NOT NULL DEFAULT 0;
DROP TABLE dbo.Old;
