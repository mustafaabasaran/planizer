-- expect: MSSQL-REV-001 severity=Critical line=5
-- expect: MSSQL-REV-001 severity=Critical line=6
-- expect: MSSQL-REV-001 severity=Critical line=7
-- expect: MSSQL-REV-001 severity=Critical line=8
DROP TABLE dbo.LegacyOrders;
ALTER TABLE dbo.Customers DROP COLUMN TaxCode;
TRUNCATE TABLE dbo.AuditLog;
DELETE FROM dbo.SessionCache;
