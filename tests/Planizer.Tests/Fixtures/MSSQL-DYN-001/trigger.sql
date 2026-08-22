-- All three dynamic execution forms: EXEC('…'), sp_executesql, EXEC @variable.
-- expect: MSSQL-DYN-001 severity=Warning line=5
-- expect: MSSQL-DYN-001 severity=Warning line=6
-- expect: MSSQL-DYN-001 severity=Warning line=7
EXEC ('DROP TABLE dbo.Unknown');
EXEC sp_executesql N'UPDATE dbo.T SET C = 1';
EXEC @procName;
