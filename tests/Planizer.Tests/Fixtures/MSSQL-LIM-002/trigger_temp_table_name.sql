-- A temporary table name may be at most 116 characters including the '#'. ScriptDom accepts
-- the 117-character name below; SQL Server rejects it with error 103.
-- expect: MSSQL-LIM-002 severity=Blocker line=4
CREATE TABLE #TTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTTT (Id int NOT NULL);
