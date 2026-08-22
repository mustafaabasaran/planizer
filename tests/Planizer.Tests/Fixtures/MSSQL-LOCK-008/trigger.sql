-- Sch-M on two different tables in one transaction: deadlock potential against
-- concurrent sessions that touch the same tables in another order.
-- expect: MSSQL-LOCK-008 severity=Warning line=5
BEGIN TRAN;
ALTER TABLE dbo.A ADD C1 int NULL;
ALTER TABLE dbo.B ADD C2 int NULL;
COMMIT;
