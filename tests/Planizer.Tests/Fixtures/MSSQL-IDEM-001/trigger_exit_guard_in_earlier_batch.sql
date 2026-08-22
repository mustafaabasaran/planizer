-- RETURN ends the batch, not the script: an exit guard before GO does not protect the next batch,
-- and an exit guard inside another IF's branch protects only that branch.
-- expect: MSSQL-IDEM-001 severity=Warning line=7
-- expect: MSSQL-IDEM-001 severity=Warning line=13
IF OBJECT_ID(N'dbo.Orders', N'U') IS NOT NULL RETURN;
GO
CREATE TABLE dbo.Orders (Id int NOT NULL PRIMARY KEY);
GO
IF @@SERVERNAME = 'PROD'
BEGIN
    IF OBJECT_ID(N'dbo.Audit', N'U') IS NOT NULL RETURN;
END
CREATE TABLE dbo.Audit (Id int NOT NULL PRIMARY KEY);
