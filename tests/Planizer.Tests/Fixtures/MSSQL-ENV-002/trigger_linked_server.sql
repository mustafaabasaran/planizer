-- Four-part names go through a linked server that only exists on some environments; one
-- Warning per statement.
-- expect: MSSQL-ENV-002 severity=Warning line=5
-- expect: MSSQL-ENV-002 severity=Warning line=6
INSERT INTO dbo.Rates (Code, Rate) SELECT Code, Rate FROM [SRV-FX].[Market].dbo.Rates;
EXEC [SRV-FX].[Market].dbo.usp_Refresh;
