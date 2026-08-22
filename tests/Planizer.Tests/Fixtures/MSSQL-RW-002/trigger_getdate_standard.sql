-- planizer-test: edition=Standard
-- On Standard the rewrite happens for ANY default (the fast path is Enterprise-only);
-- GETDATE() being a runtime constant does not save it here.
-- expect: MSSQL-RW-002 severity=Critical line=5
ALTER TABLE dbo.Orders ADD CreatedAt datetime2 NOT NULL DEFAULT GETDATE();
