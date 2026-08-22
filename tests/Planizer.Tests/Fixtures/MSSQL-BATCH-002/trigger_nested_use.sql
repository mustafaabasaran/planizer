-- Uses nested in IF bodies are compiled with their batch; both variables report on the one
-- statement that uses them. @@TRANCOUNT is a system function, not a variable.
-- expect: MSSQL-BATCH-002 severity=Blocker line=9
DECLARE @from int = 1, @to int = 2;
GO
IF @@TRANCOUNT = 0
BEGIN
    PRINT 'no transaction';
    UPDATE dbo.T SET Id = @to WHERE Id = @from;
END
