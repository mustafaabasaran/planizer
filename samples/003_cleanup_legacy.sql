-- Migration 003: remove legacy v1 objects after the v2 cutover (release 2026-08).
-- Runs inside the announced maintenance window; see samples/.planizer.json for
-- the window-specific severity overrides.

-- planizer:ignore MSSQL-REV-001 table archived to cold storage 2026-08-01 (OPS-4711)
DROP TABLE dbo.OrderAudit_Legacy;

-- planizer:ignore MSSQL-REV-003 no module references Fax; checked sys.sql_expression_dependencies 2026-08-18
EXEC sp_rename 'dbo.Customers.Fax', 'Fax_deprecated', 'COLUMN';

DELETE FROM dbo.FeatureFlags
WHERE Name LIKE N'legacy-%';

-- planizer:ignore MSSQL-REV-001, MSSQL-REV-004 staging table is reloaded from scratch on every import
TRUNCATE TABLE dbo.StagingImport;
