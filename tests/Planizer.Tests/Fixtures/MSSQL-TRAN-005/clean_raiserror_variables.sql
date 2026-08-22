-- Pre-2012 rethrow idiom: severity comes from ERROR_SEVERITY(), unknown offline, assumed to be an error.
-- expect-none: MSSQL-TRAN-005
BEGIN TRY
    ALTER TABLE dbo.A ADD C1 int NULL;
END TRY
BEGIN CATCH
    DECLARE @msg nvarchar(4000) = ERROR_MESSAGE(), @sev int = ERROR_SEVERITY(), @state int = ERROR_STATE();
    RAISERROR(@msg, @sev, @state);
END CATCH
