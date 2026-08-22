-- 13 guarded inserts are 26 flattened statements but only 13 do work: below the threshold.
-- expect-none: MSSQL-TRAN-006
BEGIN TRAN;
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 1) INSERT INTO dbo.Lookup (Id, Name) VALUES (1, 'Value 1');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 2) INSERT INTO dbo.Lookup (Id, Name) VALUES (2, 'Value 2');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 3) INSERT INTO dbo.Lookup (Id, Name) VALUES (3, 'Value 3');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 4) INSERT INTO dbo.Lookup (Id, Name) VALUES (4, 'Value 4');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 5) INSERT INTO dbo.Lookup (Id, Name) VALUES (5, 'Value 5');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 6) INSERT INTO dbo.Lookup (Id, Name) VALUES (6, 'Value 6');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 7) INSERT INTO dbo.Lookup (Id, Name) VALUES (7, 'Value 7');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 8) INSERT INTO dbo.Lookup (Id, Name) VALUES (8, 'Value 8');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 9) INSERT INTO dbo.Lookup (Id, Name) VALUES (9, 'Value 9');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 10) INSERT INTO dbo.Lookup (Id, Name) VALUES (10, 'Value 10');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 11) INSERT INTO dbo.Lookup (Id, Name) VALUES (11, 'Value 11');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 12) INSERT INTO dbo.Lookup (Id, Name) VALUES (12, 'Value 12');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 13) INSERT INTO dbo.Lookup (Id, Name) VALUES (13, 'Value 13');
COMMIT;
