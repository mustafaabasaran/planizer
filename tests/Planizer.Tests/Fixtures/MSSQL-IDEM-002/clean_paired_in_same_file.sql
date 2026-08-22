-- A safe DROP earlier in the file makes the ADD re-runnable; a column the file itself added
-- (guarded) can be dropped without a guard — the helper-column pattern.
-- expect-none: MSSQL-IDEM-002
ALTER TABLE dbo.Orders DROP COLUMN IF EXISTS StatusNew;
ALTER TABLE dbo.Orders ADD StatusNew tinyint NULL;

IF COL_LENGTH(N'dbo.Orders', N'TmpTotal') IS NULL
    ALTER TABLE dbo.Orders ADD TmpTotal money NULL;
UPDATE dbo.Orders SET TmpTotal = Total WHERE TmpTotal IS NULL;
ALTER TABLE dbo.Orders DROP COLUMN TmpTotal;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Tmp')
    ALTER TABLE dbo.Orders ADD CONSTRAINT CK_Tmp CHECK (Total >= 0);
ALTER TABLE dbo.Orders DROP CONSTRAINT CK_Tmp;
