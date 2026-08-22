-- 25 guarded inserts: the IF wrappers are not counted, the 25 INSERTs are, and 25 is the threshold.
-- expect: MSSQL-TRAN-006 severity=Info line=3
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
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 14) INSERT INTO dbo.Lookup (Id, Name) VALUES (14, 'Value 14');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 15) INSERT INTO dbo.Lookup (Id, Name) VALUES (15, 'Value 15');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 16) INSERT INTO dbo.Lookup (Id, Name) VALUES (16, 'Value 16');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 17) INSERT INTO dbo.Lookup (Id, Name) VALUES (17, 'Value 17');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 18) INSERT INTO dbo.Lookup (Id, Name) VALUES (18, 'Value 18');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 19) INSERT INTO dbo.Lookup (Id, Name) VALUES (19, 'Value 19');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 20) INSERT INTO dbo.Lookup (Id, Name) VALUES (20, 'Value 20');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 21) INSERT INTO dbo.Lookup (Id, Name) VALUES (21, 'Value 21');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 22) INSERT INTO dbo.Lookup (Id, Name) VALUES (22, 'Value 22');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 23) INSERT INTO dbo.Lookup (Id, Name) VALUES (23, 'Value 23');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 24) INSERT INTO dbo.Lookup (Id, Name) VALUES (24, 'Value 24');
IF NOT EXISTS (SELECT 1 FROM dbo.Lookup WHERE Id = 25) INSERT INTO dbo.Lookup (Id, Name) VALUES (25, 'Value 25');
COMMIT;
