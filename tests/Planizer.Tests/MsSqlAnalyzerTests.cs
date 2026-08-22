using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

public class MsSqlAnalyzerTests
{
    private const string DefaultAssumption = "SQL Server 2019, Standard edition, offline mode";

    [Fact]
    public void Parse_error_is_reported_as_blocker_finding()
    {
        var report = new MsSqlAnalyzer().Analyze(
            [("broken.sql", "CREATE TABLE dbo.T (")],
            new PlanizerConfig());

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-PARSE-001");
        Assert.Equal(Severity.Blocker, finding.Severity);
        Assert.StartsWith("Parse error:", finding.Message);
        Assert.Equal("broken.sql", finding.Location.File);
        Assert.Equal(DefaultAssumption, finding.Assumption);
        Assert.False(finding.Suppressed);
    }

    [Fact]
    public void Valid_script_with_no_rules_yields_correct_summary()
    {
        var report = new MsSqlAnalyzer(rules: []).Analyze(
            [
                ("migration.sql", """
                    BEGIN TRAN;
                    ALTER TABLE dbo.T ADD C int NULL;
                    CREATE INDEX IX_T_C ON dbo.T (C);
                    EXEC('SELECT 1');
                    UPDATE dbo.T SET C = 1;
                    COMMIT;
                    """),
            ],
            new PlanizerConfig { Rollback = true });

        Assert.Empty(report.Findings);
        Assert.Equal(6, report.Summary.StatementCount);
        Assert.Equal(2, report.Summary.DdlCount);
        // Only ALTER TABLE acquires Sch-M (brief — catalog: add_column_nullable);
        // an offline nonclustered CREATE INDEX takes an S table lock instead.
        Assert.Equal(1, report.Summary.SchMLockCount);
        Assert.Equal(1, report.Summary.UnanalyzableCount);
        Assert.Equal(0, report.Summary.IrreversibleCount);
        // Rollback runs last-change-first; the dynamic EXEC and the UPDATE have no inverse,
        // so the script is partial and incomplete.
        Assert.Equal(
            ["DROP INDEX [IX_T_C] ON [dbo].[T];", "ALTER TABLE [dbo].[T] DROP COLUMN [C];"],
            report.Summary.RollbackScript);
        Assert.False(report.Summary.RollbackComplete);
        Assert.Equal(0, report.SuppressedCount);
    }

    [Fact]
    public void Report_carries_config_derived_metadata()
    {
        var config = new PlanizerConfig
        {
            TargetVersion = SqlServerVersion.Sql2022,
            Edition = SqlEdition.Enterprise,
        };

        var report = new MsSqlAnalyzer(rules: []).Analyze([("a.sql", "SELECT 1;")], config);

        Assert.Equal(SqlDialect.MsSql, report.Dialect);
        Assert.Equal("2022", report.TargetVersion);
        Assert.Equal("Enterprise", report.Edition);
        Assert.Equal(AnalysisMode.Offline, report.Mode);
        Assert.Equal(["a.sql"], report.Files);
        Assert.False(string.IsNullOrWhiteSpace(report.ToolVersion));
    }

    [Fact]
    public void Rule_findings_are_produced_and_carry_assumption()
    {
        var report = Analyze("DROP TABLE dbo.Old;");

        var finding = Assert.Single(report.Findings);
        Assert.Equal(FakeDdlRule.RuleId, finding.RuleId);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(DefaultAssumption, finding.Assumption);
        Assert.Equal("DROP TABLE dbo.Old;", finding.StatementSummary);
        Assert.Equal(new SourceLocation("test.sql", 1, 1), finding.Location);
    }

    [Fact]
    public void Suppressed_finding_stays_in_report_and_is_counted()
    {
        var report = Analyze("-- planizer:ignore TEST-DDL-001 reviewed\nDROP TABLE dbo.Old;");

        var finding = Assert.Single(report.Findings);
        Assert.True(finding.Suppressed);
        Assert.Equal("reviewed", finding.SuppressReason);
        Assert.Equal(1, report.SuppressedCount);
    }

    [Fact]
    public void Disabled_rule_produces_no_findings()
    {
        var config = new PlanizerConfig
        {
            Rules = new Dictionary<string, RuleOverride>
            {
                [FakeDdlRule.RuleId] = new(Enabled: false),
            },
        };

        var report = Analyze("DROP TABLE dbo.Old;", config);

        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Severity_override_is_applied_to_findings()
    {
        var config = new PlanizerConfig
        {
            Rules = new Dictionary<string, RuleOverride>
            {
                [FakeDdlRule.RuleId] = new(Enabled: true, Severity: Severity.Blocker),
            },
        };

        var report = Analyze("DROP TABLE dbo.Old;", config);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(Severity.Blocker, finding.Severity);
    }

    [Fact]
    public void Statement_indices_are_global_across_files()
    {
        var report = new MsSqlAnalyzer(rules: [new FakeDdlRule()]).Analyze(
            [
                ("one.sql", "DROP TABLE dbo.A;"),
                ("two.sql", "SELECT 1;\nDROP TABLE dbo.B;"),
            ],
            new PlanizerConfig());

        Assert.Equal(2, report.Findings.Count);
        Assert.Equal(3, report.Summary.StatementCount);
        Assert.Equal(2, report.Summary.DdlCount);
        Assert.Equal(["one.sql", "two.sql"], report.Files);
    }

    [Fact]
    public void Transactions_do_not_span_files()
    {
        // File one leaves a transaction open; file two's statement must not be inside it.
        MsSqlAnalysisContext? seen = null;
        var probe = new ContextProbeRule(c => seen = c);

        new MsSqlAnalyzer(rules: [probe]).Analyze(
            [
                ("one.sql", "BEGIN TRAN;\nDROP TABLE dbo.A;"),
                ("two.sql", "DROP TABLE dbo.B;"),
            ],
            new PlanizerConfig());

        Assert.NotNull(seen);
        var scope = Assert.Single(seen.Transactions);
        Assert.Equal(0, scope.BeginIndex);
        Assert.Equal(1, scope.EndIndex);
        Assert.True(seen.IsInExplicitTransaction(1));
        Assert.False(seen.IsInExplicitTransaction(2));
    }

    private static Report Analyze(string sql, PlanizerConfig? config = null)
        => new MsSqlAnalyzer(rules: [new FakeDdlRule()])
            .Analyze([("test.sql", sql)], config ?? new PlanizerConfig());

    /// <summary>Flags every DDL statement; exists to exercise the analyzer resolution pipeline.</summary>
    private sealed class FakeDdlRule : MsSqlRuleBase
    {
        public const string RuleId = "TEST-DDL-001";

        public override string Id => RuleId;
        public override string Title => "Test rule: flags every DDL statement";
        public override Severity DefaultSeverity => Severity.Warning;

        protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
            => context.Statements
                .Where(s => s.Kind == StatementKind.Ddl)
                .Select(s => CreateFinding(s, DefaultSeverity, "DDL statement found."));
    }

    private sealed class ContextProbeRule(Action<MsSqlAnalysisContext> onAnalyze) : MsSqlRuleBase
    {
        public override string Id => "TEST-PROBE-001";
        public override string Title => "Test rule: captures the analysis context";
        public override Severity DefaultSeverity => Severity.Info;

        protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
        {
            onAnalyze(context);
            yield break;
        }
    }
}
