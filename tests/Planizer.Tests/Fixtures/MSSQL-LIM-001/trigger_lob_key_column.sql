-- LOB / MAX types can never be index key columns (error 1919). The column types are resolved
-- from the CREATE TABLE and the ALTER TABLE ADD earlier in the same file.
-- expect: MSSQL-LIM-001 severity=Blocker line=12
-- expect: MSSQL-LIM-001 severity=Blocker line=13
-- expect: MSSQL-LIM-001 severity=Blocker line=15
CREATE TABLE dbo.Documents
(
    Id int NOT NULL,
    Body nvarchar(max) NULL,
    Meta xml NULL
);
CREATE INDEX IX_Documents_Body ON dbo.Documents (Body);
ALTER TABLE dbo.Documents ADD CONSTRAINT UQ_Documents_Meta UNIQUE (Meta);
ALTER TABLE dbo.Documents ADD Notes nvarchar(max) NULL;
CREATE INDEX IX_Documents_Notes ON dbo.Documents (Id, Notes);
