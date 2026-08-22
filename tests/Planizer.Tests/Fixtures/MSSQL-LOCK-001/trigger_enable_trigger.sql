-- ENABLE / DISABLE TRIGGER flips a flag in metadata: brief Sch-M on the table (Info).
-- expect: MSSQL-LOCK-001 severity=Info line=4
-- expect: MSSQL-LOCK-001 severity=Info line=5
ALTER TABLE [dbo].[Account] ENABLE TRIGGER [cdc_CardAccount];
ALTER TABLE dbo.Account DISABLE TRIGGER ALL;
