-- Same-batch use, named EXEC parameters (@objname is sp_rename's parameter, not a variable),
-- system functions (@@ROWCOUNT) and procedure bodies are not out-of-scope uses.
-- expect-none: MSSQL-BATCH-002
DECLARE @objname nvarchar(128) = N'dbo.Customers.Fax';
SELECT @objname;
GO
EXEC sp_rename @objname = 'dbo.Customers.Fax', @newname = 'FaxNumber', @objtype = 'COLUMN';
IF @@ROWCOUNT = 0 PRINT 'nothing renamed';
GO
CREATE PROCEDURE dbo.UseObjName @objname nvarchar(128) AS SELECT @objname;
