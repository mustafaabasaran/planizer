-- [T], dbo.T and the table inside an sp_rename literal ('[T].[Col]') are one table: quoting and
-- the implicit dbo schema must not split it into "different tables" (seen on EF Core scripts).
-- expect-none: MSSQL-LOCK-008
BEGIN TRAN;
ALTER TABLE [BatchDraft] ADD [HasError] bit NULL;
EXEC sp_rename N'[BatchDraft].[IsError]', N'HadError', N'COLUMN';
ALTER TABLE dbo.BatchDraft ADD [Note] nvarchar(10) NULL;
COMMIT;
