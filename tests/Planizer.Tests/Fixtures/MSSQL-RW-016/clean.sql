-- Small fixed-width total, and the added column is variable-length: nothing to report.
-- expect-none: MSSQL-RW-016
CREATE TABLE dbo.SmallTable
(
    Id int NOT NULL,
    Name nvarchar(200) NULL
);
ALTER TABLE dbo.Orders ADD Note nvarchar(400) NULL;
