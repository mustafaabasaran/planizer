-- planizer-test: rollback=true
-- Everything here either has an automatic inverse, is already flagged as irreversible
-- (REV-001 owns TRUNCATE), is dynamic SQL (DYN-001 owns it), or is index maintenance
-- that restores the identical schema (plain REBUILD, REORGANIZE) — no REV-002 noise on top.
-- expect-none: MSSQL-REV-002
CREATE TABLE dbo.Widgets (Id int NOT NULL, Name nvarchar(100) NULL);
CREATE INDEX IX_Widgets_Name ON dbo.Widgets (Name);
ALTER TABLE dbo.Widgets ADD CONSTRAINT PK_Widgets PRIMARY KEY (Id);
ALTER INDEX IX_Widgets_Name ON dbo.Widgets REBUILD;
ALTER INDEX ALL ON dbo.Widgets REORGANIZE;
TRUNCATE TABLE dbo.Widgets;
EXEC (@sql);
SELECT 1;
