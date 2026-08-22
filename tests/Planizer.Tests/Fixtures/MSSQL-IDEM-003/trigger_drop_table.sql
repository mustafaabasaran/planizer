-- A bare DROP fails when the object is already gone: "Cannot drop the table 'dbo.Legacy', because it
-- does not exist or you do not have permission" (3701).
-- expect: MSSQL-IDEM-003 severity=Warning line=4
DROP TABLE dbo.Legacy;
