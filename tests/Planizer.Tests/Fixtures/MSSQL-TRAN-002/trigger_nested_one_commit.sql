-- Two BEGIN TRANs but a single COMMIT: @@TRANCOUNT stays 1 and the outer transaction is left open.
-- expect: MSSQL-TRAN-002 severity=Critical line=3
BEGIN TRAN;
BEGIN TRAN;
UPDATE dbo.A SET C1 = 1 WHERE Id = 1;
COMMIT;
