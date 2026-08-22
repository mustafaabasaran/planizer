-- Variable-length keys whose declared maximum exceeds the limit: CREATE succeeds with a
-- warning, but any INSERT/UPDATE producing a longer key fails later (error 1946).
-- Clustered: nvarchar(450) 900 + int 4 = 904 > 900. Nonclustered on 2019: nvarchar(900) = 1800 > 1700.
-- expect: MSSQL-LIM-001 severity=Critical line=12
-- expect: MSSQL-LIM-001 severity=Critical line=13
CREATE TABLE dbo.Names
(
    Id int NOT NULL,
    Name nvarchar(450) NOT NULL,
    Path nvarchar(900) NULL
);
CREATE UNIQUE CLUSTERED INDEX IX_Names_Name ON dbo.Names (Name, Id);
CREATE INDEX IX_Names_Path ON dbo.Names (Path);
