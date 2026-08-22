-- The dropped indexes were created NONCLUSTERED earlier in this file (CREATE INDEX defaults to
-- nonclustered): dropping them deallocates pages, no row is rewritten — nothing to report.
-- Name matching ignores quoting and the implicit dbo schema.
-- expect-none: MSSQL-RW-013
CREATE NONCLUSTERED INDEX IX_Orders_Code ON dbo.Orders (Code);
CREATE UNIQUE INDEX [IX_Orders_Number] ON [Orders] ([Number]);
GO
DROP INDEX IX_Orders_Code ON dbo.Orders;
DROP INDEX [IX_Orders_Number] ON [dbo].[Orders];
