-- PRINT / RAISERROR / THROW arguments are message text: shown in the deployment log, never
-- stored in a column, so a code-page '?' there is cosmetic and out of scope for this rule.
-- expect-none: MSSQL-LIT-001
PRINT 'Başladı';
RAISERROR('Adım 1 başladı', 0, 1) WITH NOWAIT;
IF @@ERROR <> 0 THROW 50001, 'Yükleme başarısız', 1;
