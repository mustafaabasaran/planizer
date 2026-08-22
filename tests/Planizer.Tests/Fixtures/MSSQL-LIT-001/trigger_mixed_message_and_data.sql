-- Message text (PRINT / RAISERROR / THROW arguments) never reaches a column and is not counted;
-- the INSERT literal on line 6 is, so the file gets one Warning counting exactly that one.
-- expect: MSSQL-LIT-001 severity=Warning line=6
PRINT 'Ödeme türleri yükleniyor';
RAISERROR('Adım 1 başladı', 0, 1) WITH NOWAIT;
INSERT INTO dbo.PaymentType (Code, Name) VALUES ('CASH', 'Nakit Ödeme');
IF @@ERROR <> 0 THROW 50001, 'Yükleme başarısız', 1;
