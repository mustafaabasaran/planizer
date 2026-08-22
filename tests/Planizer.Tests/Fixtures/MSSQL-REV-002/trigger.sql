-- planizer-test: rollback=true
-- Mixed script: the CREATE INDEX auto-reverses to DROP INDEX; the UPDATE and INSERT have no
-- automatic inverse and are summarised in ONE Info finding anchored at the first of them.
-- expect: MSSQL-REV-002 severity=Info line=6
CREATE INDEX IX_Orders_Status ON dbo.Orders (Status);
UPDATE dbo.Orders SET Status = 1 WHERE Status = 0;
INSERT INTO dbo.OrderLog (Note) VALUES ('migrated');
