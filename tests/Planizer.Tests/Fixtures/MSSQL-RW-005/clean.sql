-- A certain signal (MAX, NOT NULL, NULL, COLLATE) routes the statement to its own rule;
-- RW-005 only covers the bare type respecification nothing else explains.
-- expect-none: MSSQL-RW-005
ALTER TABLE dbo.Orders ALTER COLUMN Notes nvarchar(MAX);
ALTER TABLE dbo.Orders ALTER COLUMN Id bigint NOT NULL;
