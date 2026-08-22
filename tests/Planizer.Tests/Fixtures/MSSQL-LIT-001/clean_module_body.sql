-- Module bodies are definitions, not deploy-time statements: literals inside them are out of
-- scope for this rule (module bodies are never flattened).
-- expect-none: MSSQL-LIT-001
CREATE OR ALTER PROCEDURE dbo.GetLabel AS SELECT 'Ödeme' AS Label;
