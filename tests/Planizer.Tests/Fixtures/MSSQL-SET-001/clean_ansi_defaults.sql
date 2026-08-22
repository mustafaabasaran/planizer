-- SET ANSI_DEFAULTS ON turns on ANSI_NULLS and QUOTED_IDENTIFIER together.
-- expect-none: MSSQL-SET-001
SET ANSI_DEFAULTS ON;
CREATE INDEX IX_Orders_Open ON dbo.Orders (CustomerId) WHERE Status = 'Open';
