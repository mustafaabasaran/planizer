-- Only an explicit length/precision can narrow; parameterless and MAX types cannot.
-- expect-none: MSSQL-RW-006
ALTER TABLE dbo.Orders ALTER COLUMN Id bigint;
ALTER TABLE dbo.Orders ALTER COLUMN Notes nvarchar(MAX);
