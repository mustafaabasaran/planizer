-- A bare CREATE TABLE fails on the second run: "There is already an object named 'Orders'" (2714).
-- expect: MSSQL-IDEM-001 severity=Warning line=3
CREATE TABLE dbo.Orders (Id int NOT NULL PRIMARY KEY, Total money NULL);
