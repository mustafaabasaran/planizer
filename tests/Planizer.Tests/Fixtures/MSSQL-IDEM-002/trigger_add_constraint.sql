-- Re-running an unguarded ADD CONSTRAINT fails: "There is already an object named 'CK_Orders_Total'" (2714).
-- expect: MSSQL-IDEM-002 severity=Warning line=3
ALTER TABLE dbo.Orders ADD CONSTRAINT CK_Orders_Total CHECK (Total >= 0);
