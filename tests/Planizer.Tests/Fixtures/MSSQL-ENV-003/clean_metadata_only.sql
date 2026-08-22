-- Only metadata-only DDL: there is no long run to report on.
-- expect-none: MSSQL-ENV-003
ALTER TABLE dbo.Orders ADD Note nvarchar(200) NULL;
ALTER TABLE dbo.Orders DROP COLUMN LegacyFlag;
EXEC sp_rename 'dbo.Orders.Note', 'Comment', 'COLUMN';
