-- Batched delete: each iteration opens and commits its own transaction.
-- expect-none: MSSQL-TRAN-002
WHILE 1 = 1
BEGIN
    BEGIN TRAN;
    DELETE TOP (1000) FROM dbo.Log WHERE CreatedAt < '2020-01-01';
    COMMIT;
    IF @@ROWCOUNT = 0 BREAK;
END
