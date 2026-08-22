-- No explicit SET at all: whether the filtered index builds depends on the client's defaults
-- (sqlcmd/osql run with QUOTED_IDENTIFIER OFF unless -I is given).
-- expect: MSSQL-SET-001 severity=Warning line=4
CREATE INDEX IX_Orders_Open ON dbo.Orders (CustomerId) WHERE Status = 'Open';
