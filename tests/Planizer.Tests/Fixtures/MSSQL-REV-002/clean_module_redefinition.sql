-- planizer-test: rollback=true
-- Redefining source-controlled modules (CREATE OR ALTER / ALTER of procedures, views,
-- functions, triggers) has an inverse that is not derivable from this script — the previous
-- definition — but it always exists in version control and no data is at stake. The rollback
-- script carries a redeploy instruction instead of a REV-002 warning. Plain CREATE of a
-- function or trigger reverses to DROP like views and procedures do.
-- expect-none: MSSQL-REV-002
CREATE OR ALTER PROCEDURE [dbo].[usp_GetOpenOrders]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id FROM dbo.Orders WITH (NOLOCK) WHERE Status = 1;
END;
GO
ALTER VIEW dbo.V_OpenOrders AS SELECT Id FROM dbo.Orders WHERE Status = 1;
GO
CREATE OR ALTER FUNCTION dbo.fn_IsActive(@state int) RETURNS bit AS BEGIN RETURN CASE WHEN @state = 1 THEN 1 ELSE 0 END; END;
GO
ALTER TRIGGER dbo.TR_Orders_Audit ON dbo.Orders AFTER UPDATE AS BEGIN SET NOCOUNT ON; END;
GO
CREATE FUNCTION dbo.fn_One() RETURNS int AS BEGIN RETURN 1; END;
GO
CREATE TRIGGER dbo.TR_Orders_Insert ON dbo.Orders AFTER INSERT AS BEGIN SET NOCOUNT ON; END;
GO
