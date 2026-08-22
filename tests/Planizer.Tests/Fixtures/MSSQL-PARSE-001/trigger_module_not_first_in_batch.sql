-- A module definition (CREATE PROCEDURE/VIEW/FUNCTION/TRIGGER/SCHEMA) must be the first
-- statement of its batch. ScriptDom rejects anything else at parse time (error 46010), so the
-- planned MSSQL-BATCH-003 rule is not needed: MSSQL-PARSE-001 already covers it.
-- expect: MSSQL-PARSE-001 severity=Blocker line=6
SET NOCOUNT ON;
CREATE PROCEDURE dbo.P AS SELECT 1;
