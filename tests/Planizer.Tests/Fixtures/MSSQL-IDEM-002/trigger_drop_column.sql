-- Re-running an unguarded DROP COLUMN fails: "column 'LegacyCode' does not exist in table 'Orders'" (4924).
-- expect: MSSQL-IDEM-002 severity=Warning line=3
ALTER TABLE dbo.Orders DROP COLUMN LegacyCode;
