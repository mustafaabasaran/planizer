using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

/// <summary>
/// MSSQL-VER-001's grammar path in the analyzer: syntax the target grammar rejects but a newer
/// SQL Server grammar accepts is a version mismatch, not a parse error, and the script is still
/// analysed with the newer parse.
/// </summary>
public class VersionGrammarTests
{
    [Fact]
    public void Newer_syntax_is_reported_as_version_mismatch_and_analysis_continues()
    {
        var report = Analyze("DROP TABLE IF EXISTS dbo.Old;", SqlServerVersion.Sql2014);

        var version = Assert.Single(report.Findings, f => f.RuleId == MsSqlAnalyzer.VersionRuleId);
        Assert.Equal(Severity.Blocker, version.Severity);
        Assert.Contains("SQL Server 2014 grammar", version.Message);
        Assert.Contains("SQL Server 2016 grammar", version.Message);
        Assert.Contains("--target-version to 2016", version.Fix);
        Assert.Equal(new SourceLocation("m.sql", 1, 1), version.Location); // anchored to the statement, not the token
        Assert.Equal("DROP TABLE IF EXISTS dbo.Old;", version.StatementSummary);
        Assert.Equal("SQL Server 2014, Standard edition, offline mode", version.Assumption);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == MsSqlAnalyzer.ParseRuleId);
        Assert.Contains(report.Findings, f => f.RuleId == "MSSQL-LOCK-001"); // the DROP TABLE was analysed
        Assert.Equal(1, report.Summary.StatementCount);
        Assert.Equal(1, report.Summary.DdlCount);
    }

    [Fact]
    public void Grammar_finding_inside_a_block_is_anchored_to_the_innermost_statement()
    {
        var report = Analyze(
            "IF OBJECT_ID('dbo.Old') IS NOT NULL\nBEGIN\n    DROP TABLE IF EXISTS dbo.Old;\nEND",
            SqlServerVersion.Sql2014);

        var version = Assert.Single(report.Findings, f => f.RuleId == MsSqlAnalyzer.VersionRuleId);
        Assert.Equal(new SourceLocation("m.sql", 3, 5), version.Location);
    }

    [Fact]
    public void Genuinely_broken_sql_stays_a_parse_error()
    {
        var report = Analyze("CREATE TABLE dbo.T (", SqlServerVersion.Sql2014);

        Assert.Contains(report.Findings, f => f.RuleId == MsSqlAnalyzer.ParseRuleId);
        Assert.DoesNotContain(report.Findings, f => f.RuleId == MsSqlAnalyzer.VersionRuleId);
    }

    [Fact]
    public void Mixed_file_with_a_real_error_stays_a_parse_error()
    {
        // One newer-syntax statement plus one broken statement: no grammar accepts the file.
        var report = Analyze("DROP TABLE IF EXISTS dbo.Old;\nCREATE TABLE dbo.T (", SqlServerVersion.Sql2014);

        Assert.Contains(report.Findings, f => f.RuleId == MsSqlAnalyzer.ParseRuleId);
        Assert.DoesNotContain(report.Findings, f => f.RuleId == MsSqlAnalyzer.VersionRuleId);
    }

    [Fact]
    public void Catalog_finding_supersedes_the_grammar_finding_on_the_same_statement()
    {
        // The 2014 grammar rejects CREATE OR ALTER; the catalog also knows it (2016 SP1).
        var report = Analyze("CREATE OR ALTER PROCEDURE dbo.P\nAS\nSELECT 1;", SqlServerVersion.Sql2014);

        var version = Assert.Single(report.Findings, f => f.RuleId == MsSqlAnalyzer.VersionRuleId);
        Assert.Contains("CREATE OR ALTER PROCEDURE", version.Message);
        Assert.Contains("2016 SP1", version.Message);
        Assert.Equal(Severity.Blocker, version.Severity);
    }

    [Fact]
    public void Service_pack_feature_on_bare_2016_is_an_inconclusive_warning()
    {
        var report = Analyze("CREATE OR ALTER PROCEDURE dbo.P AS SELECT 1;", SqlServerVersion.Sql2016);

        var version = Assert.Single(report.Findings, f => f.RuleId == MsSqlAnalyzer.VersionRuleId);
        Assert.Equal(Severity.Warning, version.Severity);
        Assert.True(version.Inconclusive);
        Assert.Contains("patch level is unknown offline", version.Message);
    }

    [Fact]
    public void Grammar_finding_for_a_post_2022_syntax_does_not_suggest_a_target_version()
    {
        // REGEXP_LIKE as a predicate is SQL Server 2025 syntax; 2025 is not a --target-version value.
        var report = Analyze("SELECT 1 FROM dbo.T WHERE REGEXP_LIKE(A, 'x');", SqlServerVersion.Sql2022);

        var version = Assert.Single(report.Findings, f => f.RuleId == MsSqlAnalyzer.VersionRuleId);
        Assert.Contains("SQL Server 2025 grammar", version.Message);
        Assert.DoesNotContain("--target-version", version.Fix);
        Assert.DoesNotContain(report.Findings, f => f.RuleId == MsSqlAnalyzer.ParseRuleId);
    }

    [Fact]
    public void Azure_accepts_newer_syntax_silently()
    {
        var report = Analyze("SELECT 1 FROM dbo.T WHERE REGEXP_LIKE(A, 'x');", SqlServerVersion.AzureSql);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == MsSqlAnalyzer.VersionRuleId);
        Assert.DoesNotContain(report.Findings, f => f.RuleId == MsSqlAnalyzer.ParseRuleId);
        Assert.Equal(1, report.Summary.StatementCount);
    }

    [Fact]
    public void Grammar_finding_honours_rule_overrides()
    {
        var disabled = new PlanizerConfig
        {
            TargetVersion = SqlServerVersion.Sql2014,
            Rules = new Dictionary<string, RuleOverride> { [MsSqlAnalyzer.VersionRuleId] = new(Enabled: false) },
        };
        var downgraded = new PlanizerConfig
        {
            TargetVersion = SqlServerVersion.Sql2014,
            Rules = new Dictionary<string, RuleOverride> { [MsSqlAnalyzer.VersionRuleId] = new(Enabled: true, Severity: Severity.Info) },
        };

        Assert.DoesNotContain(
            new MsSqlAnalyzer().Analyze([("m.sql", "DROP TABLE IF EXISTS dbo.Old;")], disabled).Findings,
            f => f.RuleId == MsSqlAnalyzer.VersionRuleId);

        var info = Assert.Single(
            new MsSqlAnalyzer().Analyze([("m.sql", "DROP TABLE IF EXISTS dbo.Old;")], downgraded).Findings,
            f => f.RuleId == MsSqlAnalyzer.VersionRuleId);
        Assert.Equal(Severity.Info, info.Severity);
    }

    [Fact]
    public void Grammar_finding_can_be_suppressed_like_any_other()
    {
        var report = Analyze(
            "-- planizer:ignore MSSQL-VER-001 prod is 2016\nDROP TABLE IF EXISTS dbo.Old;",
            SqlServerVersion.Sql2014);

        var version = Assert.Single(report.Findings, f => f.RuleId == MsSqlAnalyzer.VersionRuleId);
        Assert.Equal(2, version.Location.Line);
        Assert.True(version.Suppressed);
        Assert.Equal("prod is 2016", version.SuppressReason);
        Assert.Equal(1, report.SuppressedCount);
    }

    private static Report Analyze(string sql, SqlServerVersion target)
        => new MsSqlAnalyzer().Analyze([("m.sql", sql)], new PlanizerConfig { TargetVersion = target });
}
