-- BEGIN and COMMIT are both conditional on the same flag: the paths that open also close.
-- expect-none: MSSQL-TRAN-002
DECLARE @useTran bit = 1;
IF @useTran = 1 BEGIN TRAN;
UPDATE dbo.A SET C1 = 1 WHERE Id = 1;
IF @useTran = 1 COMMIT;
