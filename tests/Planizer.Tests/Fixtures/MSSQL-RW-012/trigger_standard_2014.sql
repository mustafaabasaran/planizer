-- planizer-test: version=2014 edition=Standard
-- expect: MSSQL-RW-012 severity=Blocker line=3
ALTER TABLE dbo.BigTable REBUILD WITH (DATA_COMPRESSION = PAGE);
