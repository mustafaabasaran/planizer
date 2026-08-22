-- A RAISERROR with severity 16 inside CATCH is error handling, not a progress message.
-- expect: MSSQL-ENV-003 severity=Info line=5
BEGIN TRY
    BEGIN TRAN;
    CREATE INDEX IX_Orders_Customer ON dbo.Orders (CustomerId);
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    RAISERROR('index build failed', 16, 1);
END CATCH
