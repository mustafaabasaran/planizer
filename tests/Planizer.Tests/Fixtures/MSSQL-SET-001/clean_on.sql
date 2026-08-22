-- Both options are explicitly ON before the statements that need them: the combined SET form
-- counts, and a later ON wins over an earlier OFF. Nothing to report.
-- expect-none: MSSQL-SET-001
SET QUOTED_IDENTIFIER OFF;
SET ANSI_NULLS, QUOTED_IDENTIFIER ON;
GO
CREATE INDEX IX_Orders_Open ON dbo.Orders (CustomerId) WHERE Status = 'Open';
ALTER TABLE dbo.Orders ADD TotalWithTax AS (Total * 1.2) PERSISTED;
CREATE TABLE dbo.T (Id int NOT NULL, A int NULL, INDEX IX_T_A (A) WHERE A IS NOT NULL);
