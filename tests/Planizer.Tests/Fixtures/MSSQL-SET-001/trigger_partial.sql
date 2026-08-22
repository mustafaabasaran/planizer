-- Only QUOTED_IDENTIFIER is set explicitly; ANSI_NULLS still depends on the connection.
-- expect: MSSQL-SET-001 severity=Warning line=4
SET QUOTED_IDENTIFIER ON;
CREATE UNIQUE INDEX UX_Users_Email ON dbo.Users (Email) WHERE Email IS NOT NULL;
