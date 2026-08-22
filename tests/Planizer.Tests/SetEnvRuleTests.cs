using Planizer.Core;
using Planizer.MsSql;
using Planizer.MsSql.Rules.Hygiene;

namespace Planizer.Tests;

/// <summary>
/// Behaviour of the SET / ENV hygiene rules that the fixture harness cannot express: exact
/// finding counts for per-file aggregates, message contents, and edition sensitivity through
/// the same statement under two targets.
/// </summary>
public class SetEnvRuleTests
{
    private static Report Analyze(string sql, PlanizerConfig? config = null)
        => new MsSqlAnalyzer().Analyze([("m.sql", sql)], config ?? new PlanizerConfig());

    [Fact]
    public void Set001_latest_explicit_setting_wins_across_batches()
    {
        var report = Analyze(
            "SET QUOTED_IDENTIFIER ON;\nSET ANSI_NULLS ON;\nGO\nSET QUOTED_IDENTIFIER OFF;\nGO\n" +
            "CREATE INDEX IX ON dbo.T (A) WHERE A IS NOT NULL;");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-SET-001");
        Assert.Equal(Severity.Blocker, finding.Severity);
        Assert.Equal(6, finding.Location.Line);
        Assert.Contains("QUOTED_IDENTIFIER OFF earlier (QUOTED_IDENTIFIER at line 4)", finding.Message);
        Assert.DoesNotContain("ANSI_NULLS OFF", finding.Message);
    }

    [Fact]
    public void Set001_missing_setting_is_inconclusive_and_names_only_the_missing_option()
    {
        var report = Analyze("SET ANSI_NULLS ON;\nCREATE INDEX IX ON dbo.T (A) WHERE A IS NOT NULL;");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-SET-001");
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.True(finding.Inconclusive);
        Assert.Contains("never sets QUOTED_IDENTIFIER explicitly", finding.Message);
        Assert.Contains("sqlcmd", finding.Message);
    }

    [Fact]
    public void Set001_reports_each_requiring_construct_once()
    {
        var report = Analyze(
            "SET QUOTED_IDENTIFIER OFF;\n" +
            "CREATE TABLE dbo.T (Id int NOT NULL, A int NULL, B AS (A + 1) PERSISTED, C AS (A * 2) PERSISTED, INDEX IX_T_A (A) WHERE A IS NOT NULL);");

        var findings = report.Findings.Where(f => f.RuleId == "MSSQL-SET-001").ToList();
        Assert.Equal(3, findings.Count);
        Assert.Contains(findings, f => f.Message.StartsWith("PERSISTED computed column B on dbo.T", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Message.StartsWith("PERSISTED computed column C on dbo.T", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Message.StartsWith("Filtered index IX_T_A on dbo.T", StringComparison.Ordinal));
    }

    [Fact]
    public void Set002_fires_exactly_at_the_threshold_and_only_once_per_file()
    {
        var inserts = string.Join("\n", Enumerable.Range(1, SetNoCountRule.Threshold)
            .Select(i => $"INSERT INTO dbo.Seed (Id) VALUES ({i});"));

        var report = Analyze(inserts);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-SET-002");
        Assert.Equal(1, finding.Location.Line);
        Assert.StartsWith($"{SetNoCountRule.Threshold} data-modification statements", finding.Message);
    }

    [Fact]
    public void Set002_suppressed_statements_leave_the_count()
    {
        var inserts = string.Join("\n", Enumerable.Range(1, SetNoCountRule.Threshold)
            .Select(i => i == 1
                ? $"INSERT INTO dbo.Seed (Id) VALUES ({i}); -- planizer:ignore MSSQL-SET-002 counted elsewhere"
                : $"INSERT INTO dbo.Seed (Id) VALUES ({i});"));

        var report = Analyze(inserts);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSSQL-SET-002");
    }

    [Fact]
    public void Set002_counts_writes_only_and_describes_done_messages_not_round_trips()
    {
        var selects = string.Join("\n", Enumerable.Range(1, SetNoCountRule.Threshold)
            .Select(i => $"SELECT {i} AS N FROM dbo.T WHERE Id = {i};"));
        Assert.DoesNotContain(Analyze(selects).Findings, f => f.RuleId == "MSSQL-SET-002");

        var writes = string.Join("\n", Enumerable.Range(1, SetNoCountRule.Threshold)
            .Select(i => i % 2 == 0
                ? $"UPDATE dbo.Seed SET Name = N'row {i}' WHERE Id = {i};"
                : $"DELETE FROM #stage WHERE Id = {i};"));
        var finding = Assert.Single(Analyze(writes).Findings, f => f.RuleId == "MSSQL-SET-002");
        Assert.Contains("adds a DONE message per statement to the response and a line to the migration runner's log", finding.Message);
        Assert.DoesNotContain("round trip", finding.Message);
    }

    [Fact]
    public void Env002_counts_a_name_nested_in_control_flow_once()
    {
        var report = Analyze("""
            IF EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Rates')
            BEGIN
                INSERT INTO dbo.Rates (Code, Rate) SELECT Code, Rate FROM [SRV-FX].[Market].dbo.Rates;
                INSERT INTO dbo.Currency (Code) SELECT Code FROM OtherDb.dbo.Currency;
            END
            """);

        var findings = report.Findings.Where(f => f.RuleId == "MSSQL-ENV-002").ToList();
        Assert.Equal(2, findings.Count);

        var linked = Assert.Single(findings, f => f.Severity == Severity.Warning);
        Assert.Equal(3, linked.Location.Line);

        var cross = Assert.Single(findings, f => f.Severity == Severity.Info);
        Assert.Equal(4, cross.Location.Line);
        Assert.StartsWith("1 statement in this file references 1 other database by name (OtherDb)", cross.Message);
    }

    [Fact]
    public void Env002_cross_database_references_are_summarised_once_per_file()
    {
        var report = Analyze(
            "INSERT INTO dbo.Currency (Code) SELECT Code FROM [LookupDb].dbo.Currency;\n" +
            "UPDATE t SET t.Name = s.Name FROM dbo.Country t JOIN [LookupDb].dbo.Country s ON s.Code = t.Code;\n" +
            "SELECT Reporting.dbo.fn_FiscalYear(GETDATE());\n" +
            "EXEC Reporting.dbo.usp_Rebuild;");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-ENV-002");
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal(1, finding.Location.Line);
        Assert.StartsWith("4 statements in this file reference 2 other databases by name (LookupDb, Reporting)", finding.Message);
    }

    [Fact]
    public void Env002_linked_server_is_a_warning_per_statement_and_not_double_counted_as_cross_database()
    {
        var report = Analyze(
            "INSERT INTO dbo.Rates SELECT Code, Rate FROM [SRV-FX].[Market].dbo.Rates;\n" +
            "SELECT [SRV-FX].[Market].dbo.fn_Rate('EUR');");

        var findings = report.Findings.Where(f => f.RuleId == "MSSQL-ENV-002").ToList();
        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(Severity.Warning, f.Severity));
        Assert.All(findings, f => Assert.Contains("references linked server SRV-FX", f.Message));
    }

    [Fact]
    public void Env002_system_databases_and_temp_objects_are_not_environment_specific()
    {
        var report = Analyze(
            "SELECT number INTO #n FROM master.dbo.spt_values WHERE type = 'P';\n" +
            "SELECT name FROM tempdb.sys.objects;\n" +
            "SELECT * FROM msdb.dbo.sysjobs;\n" +
            "SELECT master.dbo.fn_varbintohexstr(0x01);");

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSSQL-ENV-002");
    }

    [Fact]
    public void Env002_looks_inside_module_bodies()
    {
        var report = Analyze("CREATE VIEW dbo.V AS SELECT Id FROM OtherDb.dbo.B;");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-ENV-002");
        Assert.Contains("OtherDb.dbo.B", finding.Message);
    }

    [Fact]
    public void Env003_same_statement_is_long_running_on_standard_but_not_on_enterprise()
    {
        const string sql = "ALTER TABLE dbo.Orders ADD IsArchived bit NOT NULL CONSTRAINT DF_X DEFAULT 0;";

        var standard = Analyze(sql, new PlanizerConfig { Edition = SqlEdition.Standard });
        var enterprise = Analyze(sql, new PlanizerConfig { Edition = SqlEdition.Enterprise });

        var finding = Assert.Single(standard.Findings, f => f.RuleId == "MSSQL-ENV-003");
        Assert.StartsWith("1 statement in this file rewrites, scans or builds an index over a whole table", finding.Message);
        Assert.DoesNotContain(enterprise.Findings, f => f.RuleId == "MSSQL-ENV-003");
    }

    [Fact]
    public void Env003_informational_raiserror_without_nowait_counts_as_progress()
    {
        var report = Analyze(
            "RAISERROR('step 1', 0, 1);\n" +
            "CREATE INDEX IX_Orders_Customer ON dbo.Orders (CustomerId);");

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSSQL-ENV-003");
    }
}
