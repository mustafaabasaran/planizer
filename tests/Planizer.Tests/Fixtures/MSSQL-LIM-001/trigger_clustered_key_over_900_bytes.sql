-- Fixed-width key of 1000 bytes: the clustered limit is 900 bytes in every version, and a
-- key whose minimum length already exceeds it fails at CREATE time (error 1944).
-- expect: MSSQL-LIM-001 severity=Blocker line=5
-- expect: MSSQL-LIM-001 severity=Blocker line=10
CREATE TABLE dbo.Codes
(
    Code char(1000) NOT NULL,
    CONSTRAINT PK_Codes PRIMARY KEY (Code)
);
CREATE TABLE dbo.Codes2 (Code nchar(500) NOT NULL PRIMARY KEY CLUSTERED);
