-- Two-part names, temp tables and the system databases (present on every instance) are fine.
-- expect-none: MSSQL-ENV-002
INSERT INTO dbo.T (Id) SELECT Id FROM dbo.S;
SELECT number INTO #n FROM master.dbo.spt_values WHERE type = 'P';
SELECT name FROM tempdb.sys.objects WHERE name LIKE '#n%';
EXEC dbo.usp_Local;
SELECT dbo.fn_Local(1);
