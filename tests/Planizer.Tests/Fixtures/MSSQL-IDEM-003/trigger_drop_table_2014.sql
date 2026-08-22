-- planizer-test: version=2014
-- DROP … IF EXISTS is 2016+; on 2014 the fix has to be an OBJECT_ID guard.
-- expect: MSSQL-IDEM-003 severity=Warning line=4
DROP TABLE dbo.Legacy;
