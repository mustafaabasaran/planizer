-- ON is matched by an OFF for the same table (quoting style must not matter).
-- expect-none: MSSQL-REV-005
SET IDENTITY_INSERT dbo.Orders ON;
INSERT INTO dbo.Orders (Id, Number) VALUES (1, 'A-1');
SET IDENTITY_INSERT [dbo].[Orders] OFF;
