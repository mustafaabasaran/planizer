-- Re-declared in the batch that uses it: in scope again.
-- expect-none: MSSQL-BATCH-002
DECLARE @tenant int = 1;
UPDATE dbo.Settings SET Value = N'x' WHERE TenantId = @tenant;
GO
DECLARE @tenant int = 1;
DELETE FROM dbo.Cache WHERE TenantId = @tenant;
