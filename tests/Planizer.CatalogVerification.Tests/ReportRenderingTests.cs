using Planizer.Core;

namespace Planizer.CatalogVerification.Tests;

/// <summary>Markdown report rendering; no server involved.</summary>
public sealed class ReportRenderingTests
{
    private static readonly IReadOnlyList<ProbeOutcome> SampleOutcomes =
    [
        new ProbeOutcome("add_column_nullable", "AddColumnNullableProbe", VerdictKind.Verified, "log delta 412 B"),
        new ProbeOutcome("create_nonclustered_index_offline", "CreateNonclusteredIndexOfflineProbe",
            VerdictKind.Contradicted, "reads blocked | unexpectedly"),
        new ProbeOutcome("truncate_table", "TruncateTableProbe", VerdictKind.Inconclusive, "probe crashed:\nboom"),
        new ProbeOutcome("create_nonclustered_index_online", "CreateNonclusteredIndexOnlineProbe",
            null, "not applicable on Express"),
    ];

    private static string RenderSample() =>
        VerificationReport.Render(SampleOutcomes, "Developer Edition (64-bit)", SqlEdition.Enterprise, SqlServerVersion.Sql2022);

    [Fact]
    public void Report_carries_server_and_catalog_target()
    {
        var report = RenderSample();
        Assert.Contains("Developer Edition (64-bit)", report);
        Assert.Contains("edition `Enterprise`, version `Sql2022`", report);
    }

    [Fact]
    public void Report_has_one_table_row_per_outcome()
    {
        var report = RenderSample();
        Assert.Contains("| `add_column_nullable` | AddColumnNullableProbe | Verified | log delta 412 B |", report);
        Assert.Contains("**Contradicted**", report);
        Assert.Contains("Inconclusive", report);
        Assert.Contains("| n/a | not applicable on Express |", report);
    }

    [Fact]
    public void Evidence_pipes_and_newlines_cannot_break_the_table()
    {
        var report = RenderSample();
        Assert.Contains("reads blocked \\| unexpectedly", report);
        Assert.Contains("probe crashed: boom", report);
    }

    [Fact]
    public void Summary_counts_every_verdict_kind()
    {
        var report = RenderSample();
        Assert.Contains("**Summary:** 1 verified, 1 contradicted, 1 inconclusive, 1 not applicable.", report);
    }

    [Fact]
    public void Report_states_that_only_contradicted_fails()
    {
        Assert.Contains("Only **Contradicted** fails the job", RenderSample());
    }
}
