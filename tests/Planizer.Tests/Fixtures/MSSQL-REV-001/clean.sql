-- Reversible or filtered statements must not be flagged as irreversible.
-- expect-none: MSSQL-REV-001
ALTER TABLE dbo.Customers ADD TaxCode varchar(20) NULL;
DELETE FROM dbo.SessionCache WHERE ExpiresAt < '2026-01-01';
CREATE INDEX IX_T_C ON dbo.T (C);
