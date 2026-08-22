-- A PRIMARY KEY declared inside CREATE TABLE builds its index on an empty table;
-- only ALTER TABLE ADD CONSTRAINT on an existing table triggers the rule.
-- expect-none: MSSQL-RW-015
CREATE TABLE dbo.NewTable
(
    Id int NOT NULL CONSTRAINT PK_NewTable PRIMARY KEY,
    Code nvarchar(20) NOT NULL
);
