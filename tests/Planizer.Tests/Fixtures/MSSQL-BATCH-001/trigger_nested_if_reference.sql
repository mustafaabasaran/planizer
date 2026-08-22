-- Statements nested in IF/BEGIN-END are compiled with their batch too; the guard around the
-- ADD does not help the UPDATE on line 10, which is exactly the first-run failure mode.
-- expect: MSSQL-BATCH-001 severity=Blocker line=10
IF COL_LENGTH('dbo.Orders', 'Status') IS NULL
BEGIN
    ALTER TABLE dbo.Orders ADD Status tinyint NULL;
END
IF EXISTS (SELECT 1 FROM dbo.Orders WHERE ShippedDate IS NOT NULL)
BEGIN
    UPDATE dbo.Orders SET Status = 3 WHERE ShippedDate IS NOT NULL;
END
