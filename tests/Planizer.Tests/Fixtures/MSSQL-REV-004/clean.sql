-- A batched DELETE is the safe alternative; no TRUNCATE finding.
-- expect-none: MSSQL-REV-004
DELETE FROM dbo.Staging WHERE LoadDate < '2026-01-01';
