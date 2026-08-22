-- Unnamed constraints: a re-run of an unnamed CHECK / FOREIGN KEY / UNIQUE does not fail — SQL Server
-- generates a fresh name and silently adds a duplicate. An unnamed PRIMARY KEY (1779) or DEFAULT (1781) fails.
-- expect: MSSQL-IDEM-002 severity=Warning line=8
-- expect: MSSQL-IDEM-002 severity=Warning line=9
-- expect: MSSQL-IDEM-002 severity=Warning line=10
-- expect: MSSQL-IDEM-002 severity=Warning line=11
-- expect: MSSQL-IDEM-002 severity=Warning line=12
ALTER TABLE dbo.Orders ADD CHECK (Total >= 0);
ALTER TABLE dbo.Orders ADD FOREIGN KEY (CustomerId) REFERENCES dbo.Customers (Id);
ALTER TABLE dbo.Orders ADD UNIQUE (Number);
ALTER TABLE dbo.Orders ADD PRIMARY KEY (Id);
ALTER TABLE dbo.Orders ADD DEFAULT 0 FOR Total;
