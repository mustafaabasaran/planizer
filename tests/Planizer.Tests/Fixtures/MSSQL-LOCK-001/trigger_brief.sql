-- Metadata-only operations take only a brief Sch-M and report as Info.
-- expect: MSSQL-LOCK-001 severity=Info line=3
EXEC sp_rename 'dbo.OldName', 'dbo.NewName';
