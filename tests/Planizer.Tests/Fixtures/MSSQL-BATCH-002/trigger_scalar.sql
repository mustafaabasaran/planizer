-- GO ends the scope of @tenant: the batch after it fails to compile with error 137.
-- expect: MSSQL-BATCH-002 severity=Blocker line=6
DECLARE @tenant int = 1;
UPDATE dbo.Settings SET Value = N'x' WHERE TenantId = @tenant;
GO
DELETE FROM dbo.Cache WHERE TenantId = @tenant;
