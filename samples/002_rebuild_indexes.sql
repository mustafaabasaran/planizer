-- Migration 002: index maintenance after the 2026-Q3 bulk load.
-- Rebuilds the fragmented customer index, reorganizes order lines,
-- converts the legacy events heap to a clustered table, and adds a
-- covering index for the warehouse picker screen.

ALTER INDEX IX_Orders_CustomerId ON dbo.Orders REBUILD;

ALTER INDEX ALL ON dbo.OrderLines REORGANIZE;

CREATE UNIQUE CLUSTERED INDEX IX_LegacyEvents_EventId
    ON dbo.LegacyEvents (EventId);

CREATE NONCLUSTERED INDEX IX_OrderLines_ProductId
    ON dbo.OrderLines (ProductId, WarehouseId)
    WITH (ONLINE = ON);
