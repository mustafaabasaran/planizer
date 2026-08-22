-- planizer-test: version=2019 edition=Standard
-- expect: MSSQL-RW-012 severity=Critical line=4
-- expect: MSSQL-RW-012 severity=Critical line=5
ALTER TABLE dbo.BigTable REBUILD WITH (DATA_COMPRESSION = PAGE);
ALTER INDEX IX_BigTable_Code ON dbo.BigTable REBUILD WITH (DATA_COMPRESSION = ROW);
