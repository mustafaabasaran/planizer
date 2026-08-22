-- planizer-test: version=2014
-- Before SQL Server 2016 the nonclustered key limit is 900 bytes too: nvarchar(850) = 1700.
-- expect: MSSQL-LIM-001 severity=Critical line=5
CREATE TABLE dbo.Names (Id int NOT NULL, Name nvarchar(850) NOT NULL);
CREATE INDEX IX_Names_Name ON dbo.Names (Name);
