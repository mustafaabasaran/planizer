-- Reference case for the Inconclusive mechanism: the current row width of dbo.Orders
-- is unknown offline, so the rule reports Info + Inconclusive instead of staying silent.
-- expect: MSSQL-RW-016 severity=Info line=4
ALTER TABLE dbo.Orders ADD LegacyCode char(50) NULL;
