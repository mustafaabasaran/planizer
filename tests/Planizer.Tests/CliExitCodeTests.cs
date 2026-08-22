using System.Text.Json;
using Planizer.Cli;
using Planizer.Cli.Output;
using Planizer.Core;

namespace Planizer.Tests;

/// <summary>
/// Function-level CLI tests (no process spawning): exit code calculation, report writers,
/// and .sql file discovery for directory arguments.
/// </summary>
public class CliExitCodeTests
{
    private const string Assumption = "SQL Server 2019, Standard edition, offline mode";

    // ---- ExitCodeCalculator ----

    [Fact]
    public void Critical_finding_with_critical_threshold_returns_1()
    {
        var report = MakeReport(MakeFinding(Severity.Critical));

        Assert.Equal(1, ExitCodeCalculator.Calculate(report, Severity.Critical));
    }

    [Fact]
    public void Blocker_finding_with_critical_threshold_returns_1()
    {
        var report = MakeReport(MakeFinding(Severity.Blocker));

        Assert.Equal(1, ExitCodeCalculator.Calculate(report, Severity.Critical));
    }

    [Fact]
    public void Only_warning_findings_with_critical_threshold_return_0()
    {
        var report = MakeReport(MakeFinding(Severity.Warning), MakeFinding(Severity.Info));

        Assert.Equal(0, ExitCodeCalculator.Calculate(report, Severity.Critical));
    }

    [Fact]
    public void Suppressed_critical_finding_does_not_fail_the_run()
    {
        var report = MakeReport(MakeFinding(Severity.Critical, suppressed: true));

        Assert.Equal(0, ExitCodeCalculator.Calculate(report, Severity.Critical));
    }

    [Fact]
    public void Warning_finding_with_warning_threshold_returns_1()
    {
        var report = MakeReport(MakeFinding(Severity.Warning));

        Assert.Equal(1, ExitCodeCalculator.Calculate(report, Severity.Warning));
    }

    [Fact]
    public void No_findings_returns_0()
    {
        var report = MakeReport();

        Assert.Equal(0, ExitCodeCalculator.Calculate(report, Severity.Info));
    }

    // ---- TextReportWriter ----

    [Fact]
    public void Text_writer_formats_finding_line_with_tags_fix_and_summary()
    {
        var report = MakeReport(
            MakeFinding(Severity.Critical, message: "Entire table is rewritten.", fix: "Add the column as nullable first."),
            MakeFinding(Severity.Warning, suppressed: true, line: 5, message: "Sch-M lock held."),
            MakeFinding(Severity.Info, inconclusive: true, line: 7, message: "Cannot verify row width."));

        var text = WriteText(report);

        Assert.Contains("== migration.sql ==", text);
        Assert.Contains("  migration.sql:3:1\n    Critical MSSQL-RW-002 Entire table is rewritten.", text.ReplaceLineEndings("\n"));
        Assert.Contains("fix: Add the column as nullable first.", text);
        Assert.Contains("  migration.sql:5:1\n    Warning MSSQL-RW-002 Sch-M lock held. [suppressed: reviewed]", text.ReplaceLineEndings("\n"));
        Assert.Contains("  migration.sql:7:1\n    Info MSSQL-RW-002 Cannot verify row width. [inconclusive]", text.ReplaceLineEndings("\n"));
        Assert.Contains("Summary", text);
        Assert.Contains($"Assumption: {Assumption}", text);
        Assert.Contains("2 total, 1 DDL, 1 taking Sch-M locks, 0 irreversible, 0 unanalyzable", text);
        Assert.Contains("3 total (0 Blocker, 1 Critical, 1 Warning, 1 Info), 1 suppressed", text);
    }

    [Fact]
    public void Text_writer_groups_findings_per_statement_and_shows_the_statement()
    {
        const string first = "ALTER TABLE dbo.T ALTER COLUMN UpdatedBy nvarchar(50) NULL";
        const string second = "ALTER TABLE dbo.T ALTER COLUMN CreatedBy nvarchar(50) NULL";
        var report = MakeReport(
            MakeFinding(Severity.Info, line: 2, message: "Brief Sch-M lock.", statementSummary: first),
            MakeFinding(Severity.Warning, line: 2, message: "No automatic inverse.", statementSummary: first),
            MakeFinding(Severity.Info, line: 5, message: "Brief Sch-M lock.", statementSummary: second));

        var text = WriteText(report).ReplaceLineEndings("\n");

        Assert.Contains($"  migration.sql:2:1  {first}\n    Info MSSQL-RW-002 Brief Sch-M lock.\n    Warning MSSQL-RW-002 No automatic inverse.\n", text);
        Assert.Contains($"  migration.sql:5:1  {second}\n    Info MSSQL-RW-002 Brief Sch-M lock.\n", text);
        Assert.Equal(1, CountOccurrences(text, "migration.sql:2:1"));
    }

    private static int CountOccurrences(string text, string value)
        => (text.Length - text.Replace(value, string.Empty).Length) / value.Length;

    [Fact]
    public void Text_writer_reports_files_without_findings()
    {
        var report = MakeReport() with { Files = ["clean.sql"] };

        var text = WriteText(report);

        Assert.Contains("== clean.sql ==", text);
        Assert.Contains("no findings", text);
    }

    [Fact]
    public void Text_writer_colors_severity_tokens_when_color_is_enabled()
    {
        var report = MakeReport(
            MakeFinding(Severity.Critical),
            MakeFinding(Severity.Warning, line: 5),
            MakeFinding(Severity.Info, line: 7),
            MakeFinding(Severity.Blocker, line: 9));

        var writer = new StringWriter();
        new TextReportWriter(useColor: true).Write(report, writer);
        var text = writer.ToString();

        Assert.Contains("\x1b[91mBlocker\x1b[0m", text);
        Assert.Contains("\x1b[31mCritical\x1b[0m", text);
        Assert.Contains("\x1b[33mWarning\x1b[0m", text);
        Assert.Contains("\x1b[36mInfo\x1b[0m", text);
    }

    [Fact]
    public void Text_writer_emits_no_ansi_codes_by_default()
    {
        var report = MakeReport(MakeFinding(Severity.Critical));

        Assert.DoesNotContain("\x1b[", WriteText(report));
    }

    [Theory]
    [InlineData(null, false, true)]   // no NO_COLOR, terminal → color
    [InlineData("", false, true)]     // empty NO_COLOR counts as unset
    [InlineData("1", false, false)]   // any non-empty NO_COLOR disables color
    [InlineData(null, true, false)]   // redirected output → no color
    public void Color_detection_honors_no_color_and_redirection(
        string? noColorValue, bool outputRedirected, bool expected)
    {
        Assert.Equal(expected, TextReportWriter.ShouldUseColor(noColorValue, outputRedirected));
    }

    // ---- JsonReportWriter ----

    [Fact]
    public void Json_writer_emits_camel_case_properties_and_string_enums()
    {
        var report = MakeReport(MakeFinding(Severity.Critical, fix: "Use ONLINE = ON."));

        var writer = new StringWriter();
        new JsonReportWriter().Write(report, writer);

        using var json = JsonDocument.Parse(writer.ToString());
        var root = json.RootElement;

        Assert.Equal("0.1.0", root.GetProperty("toolVersion").GetString());
        Assert.Equal("MsSql", root.GetProperty("dialect").GetString());
        Assert.Equal("2019", root.GetProperty("targetVersion").GetString());
        Assert.Equal("Standard", root.GetProperty("edition").GetString());
        Assert.Equal("Offline", root.GetProperty("mode").GetString());

        var finding = root.GetProperty("findings")[0];
        Assert.Equal("MSSQL-RW-002", finding.GetProperty("ruleId").GetString());
        Assert.Equal("Critical", finding.GetProperty("severity").GetString());
        Assert.Equal("Use ONLINE = ON.", finding.GetProperty("fix").GetString());
        Assert.Equal("migration.sql", finding.GetProperty("location").GetProperty("file").GetString());
        Assert.Equal(3, finding.GetProperty("location").GetProperty("line").GetInt32());
        Assert.False(finding.GetProperty("suppressed").GetBoolean());

        Assert.Equal(2, root.GetProperty("summary").GetProperty("statementCount").GetInt32());
        Assert.Equal(0, root.GetProperty("suppressedCount").GetInt32());
    }

    // ---- SqlFileLocator ----

    [Fact]
    public void Directory_argument_expands_to_all_sql_files_recursively_sorted_by_name()
    {
        var dir = Directory.CreateTempSubdirectory("planizer-cli-test-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllText(Path.Combine(dir, "b.sql"), "SELECT 1;");
            File.WriteAllText(Path.Combine(dir, "a.sql"), "SELECT 1;");
            File.WriteAllText(Path.Combine(dir, "sub", "c.sql"), "SELECT 1;");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "not sql");

            var files = SqlFileLocator.Resolve([dir]);

            Assert.Equal(
                [
                    Path.Combine(dir, "a.sql"),
                    Path.Combine(dir, "b.sql"),
                    Path.Combine(dir, "sub", "c.sql"),
                ],
                files);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Explicit_file_arguments_are_kept_in_argument_order()
    {
        var dir = Directory.CreateTempSubdirectory("planizer-cli-test-").FullName;
        try
        {
            var first = Path.Combine(dir, "z.sql");
            var second = Path.Combine(dir, "a.sql");
            File.WriteAllText(first, "SELECT 1;");
            File.WriteAllText(second, "SELECT 1;");

            var files = SqlFileLocator.Resolve([first, second]);

            Assert.Equal([first, second], files);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_path_throws_file_not_found()
    {
        var missing = Path.Combine(Path.GetTempPath(), "planizer-does-not-exist", "nope.sql");

        var ex = Assert.Throws<FileNotFoundException>(() => SqlFileLocator.Resolve([missing]));

        Assert.Contains(missing, ex.Message);
    }

    [Fact]
    public void Directory_without_sql_files_throws_file_not_found()
    {
        var dir = Directory.CreateTempSubdirectory("planizer-cli-test-").FullName;
        try
        {
            Assert.Throws<FileNotFoundException>(() => SqlFileLocator.Resolve([dir]));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- helpers ----

    private static string WriteText(Report report)
    {
        var writer = new StringWriter();
        new TextReportWriter().Write(report, writer);
        return writer.ToString();
    }

    private static Finding MakeFinding(
        Severity severity,
        bool suppressed = false,
        bool inconclusive = false,
        int line = 3,
        string message = "Finding message.",
        string? fix = null,
        string? statementSummary = null)
        => new()
        {
            RuleId = "MSSQL-RW-002",
            Severity = severity,
            Message = message,
            Fix = fix,
            StatementSummary = statementSummary,
            Location = new SourceLocation("migration.sql", line, 1),
            Assumption = Assumption,
            Inconclusive = inconclusive,
            Suppressed = suppressed,
            SuppressReason = suppressed ? "reviewed" : null,
        };

    private static Report MakeReport(params Finding[] findings)
        => new()
        {
            ToolVersion = "0.1.0",
            Dialect = SqlDialect.MsSql,
            TargetVersion = "2019",
            Edition = "Standard",
            Mode = AnalysisMode.Offline,
            Files = ["migration.sql"],
            Findings = findings,
            Summary = new ScriptSummary
            {
                StatementCount = 2,
                DdlCount = 1,
                SchMLockCount = 1,
            },
            SuppressedCount = findings.Count(f => f.Suppressed),
        };
}
