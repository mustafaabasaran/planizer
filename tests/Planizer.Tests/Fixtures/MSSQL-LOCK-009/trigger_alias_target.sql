-- UPDATE through an alias with a single-table FROM and no WHERE still touches every row of the
-- aliased table; the finding names dbo.ChargePackage, not "T".
-- expect: MSSQL-LOCK-009 severity=Warning line=4
UPDATE T SET DefinitionType = 1, CurrencyId = 949 FROM [dbo].[ChargePackage] T;
