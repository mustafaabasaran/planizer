-- WITH NOCHECK skips the validation scan but leaves the constraint untrusted;
-- both variants are reported, the NOCHECK one only as Info.
-- expect: MSSQL-RW-014 severity=Info line=4
ALTER TABLE dbo.Orders WITH NOCHECK
    ADD CONSTRAINT CK_Orders_Quantity CHECK (Quantity > 0);
