-- Three-part names reach into another database by name (tenant placeholders included); the
-- file gets ONE Info anchored at the first such statement. Function calls count too.
-- expect: MSSQL-ENV-002 severity=Info line=4
INSERT INTO dbo.Currency (Code) SELECT Code FROM [LookupDb].dbo.Currency;
UPDATE t SET t.Name = s.Name FROM dbo.Country t JOIN [LookupDb].dbo.Country s ON s.Code = t.Code;
SELECT Reporting.dbo.fn_FiscalYear(GETDATE());
EXEC Reporting.dbo.usp_Rebuild;
