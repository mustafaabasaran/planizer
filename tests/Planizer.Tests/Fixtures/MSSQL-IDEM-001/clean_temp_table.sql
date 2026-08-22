-- Temp tables are session-scoped; a fresh deployment session never sees a previous run's #table.
-- expect-none: MSSQL-IDEM-001
CREATE TABLE #work (Id int NOT NULL);
CREATE INDEX IX_work ON #work (Id);
INSERT INTO #work (Id) SELECT Id FROM dbo.Orders;
DROP TABLE #work;
