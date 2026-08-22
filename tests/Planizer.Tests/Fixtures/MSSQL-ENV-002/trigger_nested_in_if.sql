-- Names nested in IF / BEGIN…END are reported once, for the statement that contains them — not
-- again for each enclosing wrapper. The Info counts one statement (line 8), the Warning sits on line 7.
-- expect: MSSQL-ENV-002 severity=Warning line=7
-- expect: MSSQL-ENV-002 severity=Info line=8
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Rates')
BEGIN
    INSERT INTO dbo.Rates (Code, Rate) SELECT Code, Rate FROM [SRV-FX].[Market].dbo.Rates;
    INSERT INTO dbo.Currency (Code) SELECT Code FROM OtherDb.dbo.Currency;
END
