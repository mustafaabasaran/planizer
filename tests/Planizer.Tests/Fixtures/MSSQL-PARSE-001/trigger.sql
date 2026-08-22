-- Malformed SQL: the analyzer itself (not a rule class) reports every ScriptDom
-- parse error as a Blocker finding.
-- expect: MSSQL-PARSE-001 severity=Blocker
ALTER TABLE dbo.Orders ADD;
