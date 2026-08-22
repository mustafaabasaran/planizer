-- N-prefixed literals are nvarchar and keep every character; ASCII-only varchar literals are
-- safe in any code page.
-- expect-none: MSSQL-LIT-001
INSERT INTO dbo.PaymentType (Code, Name) VALUES ('CASH', N'Nakit Ödeme');
UPDATE dbo.PaymentType SET Description = 'Cash payment' WHERE Code = 'CASH';
