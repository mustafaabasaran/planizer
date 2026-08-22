-- planizer-test: rollback=true
-- Both have a derivable inverse: DISABLE TRIGGER reverses ENABLE TRIGGER, and an sp_rename whose
-- @objname is bracketed (EF Core style) reverses like the bare form.
-- expect-none: MSSQL-REV-002
ALTER TABLE [dbo].[Account] ENABLE TRIGGER [cdc_CardAccount];
EXEC sp_rename N'[BatchAccountingEntryDraft].[IsError]', N'HasError', N'COLUMN';
EXEC sp_rename N'[Widget].[IX_Widget_Old]', N'IX_Widget_New', 'INDEX';
