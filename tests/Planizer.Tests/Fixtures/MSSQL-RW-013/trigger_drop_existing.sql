-- WITH (DROP_EXISTING = ON) also succeeds on a table with an existing clustered index:
-- still a full rewrite, but nonclustered indexes are rebuilt only if the key changes.
-- expect: MSSQL-RW-013 severity=Critical line=4
CREATE CLUSTERED INDEX IX_Orders_Id ON dbo.Orders (Id) WITH (DROP_EXISTING = ON);
