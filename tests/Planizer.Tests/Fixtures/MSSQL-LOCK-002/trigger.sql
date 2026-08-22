-- Offline index build on Standard: nonclustered blocks writes, clustered blocks everything.
-- expect: MSSQL-LOCK-002 severity=Warning line=4
-- expect: MSSQL-LOCK-002 severity=Warning line=5
CREATE INDEX IX_T_C ON dbo.T (C);
CREATE CLUSTERED INDEX CX_T ON dbo.T (C);
