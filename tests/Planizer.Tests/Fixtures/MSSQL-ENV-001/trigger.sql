-- USE pins the script to a database name that differs per environment; the migration runner
-- already chose the target database.
-- expect: MSSQL-ENV-001 severity=Info line=4
USE [Accounting_Dev];
GO
CREATE TABLE dbo.T (Id int NOT NULL);
