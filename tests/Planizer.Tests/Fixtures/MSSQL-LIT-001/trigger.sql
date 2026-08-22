-- Two varchar literals with non-ASCII characters produce one Warning per file, anchored at
-- the first statement and counting both; the N'…' literal on line 6 is fine and the PRINT on
-- line 7 is message text, which is not counted.
-- expect: MSSQL-LIT-001 severity=Warning line=5
INSERT INTO dbo.PaymentType (Code, Name) VALUES ('CASH', 'Nakit Ödeme');
INSERT INTO dbo.PaymentType (Code, Name) VALUES ('CARD', 'Kredi Kartı');
UPDATE dbo.PaymentType SET Description = N'Açıklama' WHERE Code = 'CASH';
PRINT 'Ödeme türleri yüklendi';
