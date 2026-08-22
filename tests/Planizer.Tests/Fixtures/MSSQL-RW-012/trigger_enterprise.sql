-- planizer-test: version=2014 edition=Enterprise
-- expect: MSSQL-RW-012 severity=Critical line=3
ALTER TABLE dbo.BigTable REBUILD WITH (DATA_COMPRESSION = PAGE);
