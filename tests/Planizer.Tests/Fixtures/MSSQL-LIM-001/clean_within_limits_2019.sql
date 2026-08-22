-- planizer-test: version=2019
-- nvarchar(850) = 1700 bytes fits the 2016+ nonclustered limit exactly; the MAX column sits in
-- INCLUDE, not in the key; the inline PRIMARY KEY is a 4-byte int.
-- expect-none: MSSQL-LIM-001
CREATE TABLE dbo.Names
(
    Id int NOT NULL PRIMARY KEY,
    Name nvarchar(850) NOT NULL,
    Notes nvarchar(max) NULL,
    INDEX IX_Names_Id NONCLUSTERED (Id) INCLUDE (Name)
);
CREATE INDEX IX_Names_Name ON dbo.Names (Name) INCLUDE (Notes);
CREATE UNIQUE INDEX IX_Names_Id ON dbo.Names (Id) WHERE Notes IS NOT NULL;
