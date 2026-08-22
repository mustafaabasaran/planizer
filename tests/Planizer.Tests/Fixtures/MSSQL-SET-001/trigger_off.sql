-- QUOTED_IDENTIFIER was switched OFF earlier in the script (SET options survive GO): creating a
-- filtered index now fails with error 1934 whatever the connection defaults are.
-- expect: MSSQL-SET-001 severity=Blocker line=6
SET QUOTED_IDENTIFIER OFF;
GO
CREATE INDEX IX_Orders_Open ON dbo.Orders (CustomerId) WHERE Status = 'Open';
