-- A default for an EXISTING column does not touch existing rows: metadata-only, brief Sch-M (Info),
-- not an inconclusive warning. EF Core emits the unnamed form on every default change.
-- expect: MSSQL-LOCK-001 severity=Info line=5
-- expect: MSSQL-LOCK-001 severity=Info line=6
ALTER TABLE [FinancialTransaction] ADD DEFAULT 0 FOR [CustomerNumber];
ALTER TABLE dbo.Orders ADD CONSTRAINT DF_Orders_Status DEFAULT (0) FOR Status;
