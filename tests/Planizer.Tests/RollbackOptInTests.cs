using System.Text.Json;
using Planizer.Cli.Output;
using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

/// <summary>Rollback analysis is opt-in (--rollback / "rollback": true) — ADR-0003.</summary>
public class RollbackOptInTests
{
    private const string Sql = "CREATE INDEX IX_T_C ON dbo.T (C);\nUPDATE dbo.T SET C = 1 WHERE Id = 1;\nDROP TABLE dbo.Old;";

    [Fact]
    public void Off_by_default_no_rev002_no_script_null_status_but_rev001_still_fires()
    {
        var report = new MsSqlAnalyzer().Analyze([("m.sql", Sql)], new PlanizerConfig());

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSSQL-REV-002");
        Assert.Contains(report.Findings, f => f.RuleId == "MSSQL-REV-001");
        Assert.Null(report.Summary.RollbackComplete);
        Assert.Empty(report.Summary.RollbackScript);
        Assert.Equal(1, report.Summary.IrreversibleCount);
    }

    [Fact]
    public void On_request_rev002_fires_and_script_is_generated()
    {
        var report = new MsSqlAnalyzer().Analyze([("m.sql", Sql)], new PlanizerConfig { Rollback = true });

        Assert.Contains(report.Findings, f => f.RuleId == "MSSQL-REV-002");
        Assert.False(report.Summary.RollbackComplete);
        Assert.Contains("DROP INDEX [IX_T_C] ON [dbo].[T];", report.Summary.RollbackScript);
    }

    [Fact]
    public void Config_file_and_cli_override_turn_it_on()
    {
        var fromFile = ConfigLoader.Parse("""{ "rollback": true }""");
        Assert.True(fromFile.Rollback);

        var overridden = ConfigLoader.ApplyOverrides(new PlanizerConfig(), rollback: true);
        Assert.True(overridden.Rollback);

        var untouched = ConfigLoader.ApplyOverrides(fromFile, rollback: null);
        Assert.True(untouched.Rollback);
    }

    [Fact]
    public void Writers_omit_rollback_when_not_analyzed_and_show_it_when_requested()
    {
        var off = new MsSqlAnalyzer().Analyze([("m.sql", Sql)], new PlanizerConfig());
        var on = new MsSqlAnalyzer().Analyze([("m.sql", Sql)], new PlanizerConfig { Rollback = true });

        Assert.DoesNotContain("Rollback:", Write(new TextReportWriter(), off));
        Assert.Contains("Rollback:   incomplete", Write(new TextReportWriter(), on));

        var markdownOff = Write(new MarkdownReportWriter(), off);
        Assert.DoesNotContain("Rollback script", markdownOff);
        Assert.DoesNotContain("rollback", markdownOff.Split("**Summary:**")[1]);
        Assert.Contains("Rollback script", Write(new MarkdownReportWriter(), on));

        using var json = JsonDocument.Parse(Write(new JsonReportWriter(), off));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("summary").GetProperty("rollbackComplete").ValueKind);
    }

    private static string Write(IReportWriter writer, Report report)
    {
        var output = new StringWriter();
        writer.Write(report, output);
        return output.ToString();
    }
}
