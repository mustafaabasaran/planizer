-- The early-exit idiom guards DROP too: when the object is already gone the batch ends before the DROP.
-- expect-none: MSSQL-IDEM-003
IF OBJECT_ID(N'dbo.Legacy', N'U') IS NULL RETURN;
DROP TABLE dbo.Legacy;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_Total' AND object_id = OBJECT_ID(N'dbo.Orders'))
BEGIN
    PRINT 'index already dropped';
    RETURN;
END
DROP INDEX IX_Orders_Total ON dbo.Orders;
