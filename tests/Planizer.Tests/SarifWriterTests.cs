using System.Text.Json;
using Planizer.Cli.Output;
using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

/// <summary>
/// SARIF 2.1.0 writer: schema-mandatory fields, severity→level mapping, in-source suppressions,
/// root-relative URIs and a rules array every ruleIndex resolves into.
/// </summary>
public class SarifWriterTests
{
    private const string Assumption = "SQL Server 2019, Standard edition, offline mode";

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "planizer-sarif-root");

    private static readonly IReadOnlyList<SarifRuleDescriptor> Rules =
    [
        new("MSSQL-LOCK-001", "Statement takes a Sch-M lock", Severity.Warning),
        new("MSSQL-RW-002", "NOT NULL column with default rewrites the table", Severity.Critical),
    ];

    [Fact]
    public void Writes_schema_version_tool_driver_and_one_run()
    {
        using var sarif = Write(MakeReport(MakeFinding(Severity.Warning)));
        var root = sarif.RootElement;

        Assert.Equal("https://json.schemastore.org/sarif-2.1.0.json", root.GetProperty("$schema").GetString());
        Assert.Equal("2.1.0", root.GetProperty("version").GetString());

        var runs = root.GetProperty("runs");
        Assert.Equal(1, runs.GetArrayLength());

        var driver = runs[0].GetProperty("tool").GetProperty("driver");
        Assert.Equal("Planizer", driver.GetProperty("name").GetString());
        Assert.Equal("0.1.0", driver.GetProperty("semanticVersion").GetString());
        Assert.StartsWith("https://", driver.GetProperty("informationUri").GetString());

        var invocation = runs[0].GetProperty("invocations")[0];
        Assert.True(invocation.GetProperty("executionSuccessful").GetBoolean());

        var srcRoot = runs[0].GetProperty("originalUriBaseIds").GetProperty("%SRCROOT%").GetProperty("uri").GetString();
        Assert.StartsWith("file://", srcRoot);
        Assert.EndsWith("/", srcRoot);
    }

    [Fact]
    public void Rules_array_carries_id_name_description_level_and_docs_link()
    {
        using var sarif = Write(MakeReport());
        var rules = sarif.RootElement.GetProperty("runs")[0].GetProperty("tool").GetProperty("driver").GetProperty("rules");

        Assert.Equal(2, rules.GetArrayLength());
        var rule = rules[1];
        Assert.Equal("MSSQL-RW-002", rule.GetProperty("id").GetString());
        Assert.Equal("NOT NULL column with default rewrites the table", rule.GetProperty("name").GetString());
        Assert.Equal("NOT NULL column with default rewrites the table", rule.GetProperty("shortDescription").GetProperty("text").GetString());
        Assert.Equal("error", rule.GetProperty("defaultConfiguration").GetProperty("level").GetString());
        Assert.Equal("docs/rules/MSSQL-RW-002.md", rule.GetProperty("properties").GetProperty("docs").GetString());
    }

    [Theory]
    [InlineData(Severity.Info, "note")]
    [InlineData(Severity.Warning, "warning")]
    [InlineData(Severity.Critical, "error")]
    [InlineData(Severity.Blocker, "error")]
    public void Severity_maps_to_sarif_level(Severity severity, string expectedLevel)
    {
        using var sarif = Write(MakeReport(MakeFinding(severity)));
        var result = Results(sarif)[0];

        Assert.Equal(expectedLevel, result.GetProperty("level").GetString());
        Assert.Equal(severity.ToString(), result.GetProperty("properties").GetProperty("severity").GetString());
        Assert.Equal(expectedLevel, SarifReportWriter.ToLevel(severity));
    }

    [Fact]
    public void Result_points_at_its_rule_by_index_and_appends_fix_to_message()
    {
        using var sarif = Write(MakeReport(
            MakeFinding(Severity.Critical, message: "Entire table is rewritten.", fix: "Add the column as nullable first.")));
        var result = Results(sarif)[0];

        Assert.Equal("MSSQL-RW-002", result.GetProperty("ruleId").GetString());
        Assert.Equal(1, result.GetProperty("ruleIndex").GetInt32());
        Assert.Equal(
            "Entire table is rewritten.\n\nFix: Add the column as nullable first.",
            result.GetProperty("message").GetProperty("text").GetString());

        var properties = result.GetProperty("properties");
        Assert.Equal(Assumption, properties.GetProperty("assumption").GetString());
        Assert.False(properties.GetProperty("inconclusive").GetBoolean());
        Assert.Equal("ALTER TABLE dbo.T ADD C int NOT NULL DEFAULT 0;", properties.GetProperty("statement").GetString());
    }

    [Fact]
    public void Message_without_fix_has_no_fix_suffix_and_inconclusive_is_flagged()
    {
        using var sarif = Write(MakeReport(MakeFinding(Severity.Info, message: "Cannot verify row width.", inconclusive: true)));
        var result = Results(sarif)[0];

        Assert.Equal("Cannot verify row width.", result.GetProperty("message").GetProperty("text").GetString());
        Assert.True(result.GetProperty("properties").GetProperty("inconclusive").GetBoolean());
    }

    [Fact]
    public void Suppressed_finding_carries_inSource_suppression_with_justification()
    {
        using var sarif = Write(MakeReport(
            MakeFinding(Severity.Warning, suppressed: true, suppressReason: "maintenance window OPS-1"),
            MakeFinding(Severity.Warning, line: 9)));
        var results = Results(sarif);

        var suppression = results[0].GetProperty("suppressions")[0];
        Assert.Equal("inSource", suppression.GetProperty("kind").GetString());
        Assert.Equal("maintenance window OPS-1", suppression.GetProperty("justification").GetString());

        Assert.False(results[1].TryGetProperty("suppressions", out _));
    }

    [Fact]
    public void Suppression_without_reason_omits_justification()
    {
        using var sarif = Write(MakeReport(MakeFinding(Severity.Warning, suppressed: true)));
        var suppression = Results(sarif)[0].GetProperty("suppressions")[0];

        Assert.Equal("inSource", suppression.GetProperty("kind").GetString());
        Assert.False(suppression.TryGetProperty("justification", out _));
    }

    [Fact]
    public void Location_is_root_relative_slash_separated_with_srcroot_base()
    {
        var file = Path.Combine("migrations", "2026", "001 add column.sql");
        using var sarif = Write(MakeReport(MakeFinding(Severity.Warning, file: file, line: 12, column: 5)));
        var physical = Results(sarif)[0].GetProperty("locations")[0].GetProperty("physicalLocation");

        var artifact = physical.GetProperty("artifactLocation");
        Assert.Equal("migrations/2026/001%20add%20column.sql", artifact.GetProperty("uri").GetString());
        Assert.Equal("%SRCROOT%", artifact.GetProperty("uriBaseId").GetString());

        var region = physical.GetProperty("region");
        Assert.Equal(12, region.GetProperty("startLine").GetInt32());
        Assert.Equal(5, region.GetProperty("startColumn").GetInt32());
    }

    [Fact]
    public void Absolute_path_under_root_becomes_relative()
    {
        var file = Path.Combine(Root, "db", "m.sql");
        using var sarif = Write(MakeReport(MakeFinding(Severity.Warning, file: file)));
        var artifact = Results(sarif)[0].GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("artifactLocation");

        Assert.Equal("db/m.sql", artifact.GetProperty("uri").GetString());
        Assert.Equal("%SRCROOT%", artifact.GetProperty("uriBaseId").GetString());
    }

    [Fact]
    public void File_outside_root_uses_absolute_file_uri_without_base_id()
    {
        var file = Path.Combine(Path.GetTempPath(), "elsewhere", "m.sql");
        using var sarif = Write(MakeReport(MakeFinding(Severity.Warning, file: file)));
        var artifact = Results(sarif)[0].GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("artifactLocation");

        Assert.StartsWith("file://", artifact.GetProperty("uri").GetString());
        Assert.EndsWith("/elsewhere/m.sql", artifact.GetProperty("uri").GetString());
        Assert.False(artifact.TryGetProperty("uriBaseId", out _));
    }

    [Fact]
    public void Zero_line_or_column_is_clamped_to_one()
    {
        using var sarif = Write(MakeReport(MakeFinding(Severity.Blocker, line: 0, column: 0)));
        var region = Results(sarif)[0].GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("region");

        Assert.Equal(1, region.GetProperty("startLine").GetInt32());
        Assert.Equal(1, region.GetProperty("startColumn").GetInt32());
    }

    [Fact]
    public void Unknown_rule_id_is_appended_to_rules_so_ruleIndex_resolves()
    {
        var finding = MakeFinding(Severity.Warning) with { RuleId = "MSSQL-XYZ-999" };
        using var sarif = Write(MakeReport(finding));

        var rules = sarif.RootElement.GetProperty("runs")[0].GetProperty("tool").GetProperty("driver").GetProperty("rules");
        var result = Results(sarif)[0];
        var index = result.GetProperty("ruleIndex").GetInt32();

        Assert.Equal(3, rules.GetArrayLength());
        Assert.Equal("MSSQL-XYZ-999", rules[index].GetProperty("id").GetString());
    }

    [Fact]
    public void Empty_report_has_empty_results_array()
    {
        using var sarif = Write(MakeReport());

        Assert.Equal(0, Results(sarif).GetArrayLength());
    }

    [Fact]
    public void Non_ascii_text_is_written_unescaped()
    {
        var writer = new StringWriter();
        new SarifReportWriter(Rules, Root).Write(MakeReport(MakeFinding(Severity.Warning, message: "Literal 'Ödeme' lacks N.")), writer);

        Assert.Contains("Literal 'Ödeme' lacks N.", writer.ToString());
    }

    [Fact]
    public void MsSql_rule_set_lists_every_discovered_rule_plus_parse_001_sorted()
    {
        var rules = SarifReportWriter.MsSqlRules();

        Assert.Equal(MsSqlAnalyzer.DiscoverRules().Count + 1, rules.Count);
        Assert.Contains(rules, r => r.Id == MsSqlAnalyzer.ParseRuleId && r.DefaultSeverity == Severity.Blocker);
        Assert.Contains(rules, r => r.Id == "MSSQL-LOCK-001");
        Assert.Equal(rules.Select(r => r.Id).OrderBy(id => id, StringComparer.Ordinal), rules.Select(r => r.Id));
        Assert.Equal(rules.Count, rules.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void End_to_end_analyzer_report_renders_with_real_rule_indices()
    {
        var report = new MsSqlAnalyzer().Analyze(
            [("mig.sql", "ALTER TABLE dbo.Orders ADD Status int NOT NULL DEFAULT 0;")],
            new PlanizerConfig());

        using var sarif = Write(report, SarifReportWriter.MsSqlRules());
        var run = sarif.RootElement.GetProperty("runs")[0];
        var rules = run.GetProperty("tool").GetProperty("driver").GetProperty("rules");

        Assert.NotEmpty(report.Findings);
        foreach (var result in run.GetProperty("results").EnumerateArray())
        {
            var index = result.GetProperty("ruleIndex").GetInt32();
            Assert.Equal(result.GetProperty("ruleId").GetString(), rules[index].GetProperty("id").GetString());
        }
    }

    // ---- helpers ----

    private static JsonDocument Write(Report report, IReadOnlyList<SarifRuleDescriptor>? rules = null)
    {
        var writer = new StringWriter();
        new SarifReportWriter(rules ?? Rules, Root).Write(report, writer);
        return JsonDocument.Parse(writer.ToString());
    }

    private static JsonElement Results(JsonDocument sarif)
        => sarif.RootElement.GetProperty("runs")[0].GetProperty("results");

    private static Finding MakeFinding(
        Severity severity,
        bool suppressed = false,
        bool inconclusive = false,
        string file = "migration.sql",
        int line = 3,
        int column = 1,
        string message = "Finding message.",
        string? fix = null,
        string? suppressReason = null)
        => new()
        {
            RuleId = "MSSQL-RW-002",
            Severity = severity,
            Message = message,
            Fix = fix,
            Location = new SourceLocation(file, line, column),
            StatementSummary = "ALTER TABLE dbo.T ADD C int NOT NULL DEFAULT 0;",
            Assumption = Assumption,
            Inconclusive = inconclusive,
            Suppressed = suppressed,
            SuppressReason = suppressReason,
        };

    private static Report MakeReport(params Finding[] findings)
        => new()
        {
            ToolVersion = "0.1.0",
            Dialect = SqlDialect.MsSql,
            TargetVersion = "2019",
            Edition = "Standard",
            Mode = AnalysisMode.Offline,
            Files = findings.Select(f => f.Location.File).Distinct().ToList(),
            Findings = findings,
            Summary = new ScriptSummary { StatementCount = findings.Length, RollbackComplete = true },
            SuppressedCount = findings.Count(f => f.Suppressed),
        };
}
