using Planizer.Cli;
using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

/// <summary>
/// End-to-end pins for the <c>samples/</c> migrations that the README and the CI self-check rely
/// on. They run through the full analyzer with the CLI defaults (2019, Standard, fail on
/// Critical) — not through <c>samples/.planizer.json</c> — so a rule change that silently turns
/// the "good" sample into a failing one, or stops reporting the bugs the "bad" sample was written
/// to show, is caught here rather than in CI's self-check job.
/// </summary>
public class SamplesTests
{
    private static readonly string SamplesRoot = FindSamplesRoot();

    [Fact]
    public void Every_sample_parses_without_a_parse_finding()
    {
        var files = Directory.EnumerateFiles(SamplesRoot, "*.sql").OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.True(files.Count >= 5, $"expected at least five samples under {SamplesRoot}");

        var report = new MsSqlAnalyzer().Analyze(
            files.Select(path => (Path.GetFileName(path), File.ReadAllText(path))).ToList(),
            new PlanizerConfig());

        Assert.DoesNotContain(report.Findings, f => f.RuleId == MsSqlAnalyzer.ParseRuleId);
    }

    [Fact]
    public void Idempotent_sample_passes_at_the_default_threshold()
    {
        var report = Analyze("004_idempotent_migration.sql");

        Assert.Equal(0, ExitCodeCalculator.Calculate(report, Severity.Critical));
        Assert.DoesNotContain(report.Findings, f => f.Severity >= Severity.Critical);

        // The guards, CREATE OR ALTER, the GO before the backfill, XACT_ABORT and the
        // ROLLBACK + THROW in CATCH each silence one Phase 1.5 family.
        foreach (var quiet in new[]
                 {
                     "MSSQL-IDEM-001", "MSSQL-IDEM-002", "MSSQL-IDEM-003",
                     "MSSQL-BATCH-001", "MSSQL-BATCH-002",
                     "MSSQL-TRAN-001", "MSSQL-TRAN-002", "MSSQL-TRAN-003", "MSSQL-TRAN-004", "MSSQL-TRAN-005",
                     "MSSQL-SET-001", "MSSQL-SET-002", "MSSQL-ENV-001", "MSSQL-ENV-002", "MSSQL-LIT-001",
                 })
        {
            Assert.DoesNotContain(report.Findings, f => f.RuleId == quiet);
        }
    }

    [Fact]
    public void Bad_batching_sample_reports_each_mistake_it_was_written_to_show()
    {
        var report = Analyze("005_bad_batching.sql");

        Assert.Equal(1, ExitCodeCalculator.Calculate(report, Severity.Blocker));

        var batch001 = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-BATCH-001");
        Assert.Equal(Severity.Blocker, batch001.Severity);
        Assert.Equal(27, batch001.Location.Line);
        Assert.Contains("PaymentTypeId", batch001.Message);

        var batch002 = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-BATCH-002");
        Assert.Equal(Severity.Blocker, batch002.Severity);
        Assert.Contains("@now", batch002.Message);

        var literal = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LIT-001");
        Assert.Contains("2 string literals", literal.Message);
        Assert.Contains("Kredi Kartı", literal.Message);

        var crossDb = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-ENV-002");
        Assert.Equal(Severity.Info, crossDb.Severity);
        Assert.Contains("LegacyDb", crossDb.Message);

        Assert.Single(report.Findings, f => f.RuleId == "MSSQL-ENV-001");
        Assert.Single(report.Findings, f => f.RuleId == "MSSQL-IDEM-001");
        Assert.Single(report.Findings, f => f.RuleId == "MSSQL-IDEM-002");
        Assert.Single(report.Findings, f => f.RuleId == "MSSQL-TRAN-001");
        Assert.Single(report.Findings, f => f.RuleId == "MSSQL-TRAN-003");
        Assert.DoesNotContain(report.Findings, f => f.Suppressed);
    }

    private static Report Analyze(string fileName)
    {
        var path = Path.Combine(SamplesRoot, fileName);
        return new MsSqlAnalyzer().Analyze([(fileName, File.ReadAllText(path))], new PlanizerConfig());
    }

    /// <summary>Walks up from the test binary to the repository root (the directory holding Planizer.sln).</summary>
    private static string FindSamplesRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Planizer.sln")))
            {
                return Path.Combine(dir.FullName, "samples");
            }
        }

        throw new DirectoryNotFoundException("Planizer.sln not found above " + AppContext.BaseDirectory);
    }
}
