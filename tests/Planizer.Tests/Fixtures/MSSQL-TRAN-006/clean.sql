-- Ten statements in one transaction: short enough.
-- expect-none: MSSQL-TRAN-006
BEGIN TRAN;
INSERT INTO dbo.Lookup (Id, Name) VALUES (1, 'Value 1');
INSERT INTO dbo.Lookup (Id, Name) VALUES (2, 'Value 2');
INSERT INTO dbo.Lookup (Id, Name) VALUES (3, 'Value 3');
INSERT INTO dbo.Lookup (Id, Name) VALUES (4, 'Value 4');
INSERT INTO dbo.Lookup (Id, Name) VALUES (5, 'Value 5');
INSERT INTO dbo.Lookup (Id, Name) VALUES (6, 'Value 6');
INSERT INTO dbo.Lookup (Id, Name) VALUES (7, 'Value 7');
INSERT INTO dbo.Lookup (Id, Name) VALUES (8, 'Value 8');
INSERT INTO dbo.Lookup (Id, Name) VALUES (9, 'Value 9');
INSERT INTO dbo.Lookup (Id, Name) VALUES (10, 'Value 10');
COMMIT;
