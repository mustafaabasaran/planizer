using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.CatalogVerification.Tests;

/// <summary>
/// Every probe's OperationKey must resolve to a catalog row for the editions the CI matrix
/// runs (Developer maps to Enterprise; Express), and the exemplar rows must carry the
/// expectations the probes measure against. No server involved.
/// </summary>
public sealed class CatalogLookupTests
{
    private static readonly DdlBehaviorCatalog Catalog = DdlBehaviorCatalog.Load();

    [Theory]
    [InlineData(SqlEdition.Enterprise)]
    [InlineData(SqlEdition.Express)]
    public void Every_discovered_probe_key_resolves_to_a_catalog_row(SqlEdition edition)
    {
        foreach (var probe in ProbeRunner.DiscoverProbes())
        {
            var row = Catalog.Lookup(probe.OperationKey, SqlServerVersion.Sql2022, edition);
            Assert.True(row is not null, $"no catalog row for '{probe.OperationKey}' on {edition}/Sql2022");
        }
    }

    [Fact]
    public void Add_column_nullable_is_metadata_only_on_both_ci_editions()
    {
        foreach (var edition in new[] { SqlEdition.Enterprise, SqlEdition.Express })
        {
            var row = Catalog.Lookup(DdlOperationKeys.AddColumnNullable, SqlServerVersion.Sql2022, edition);
            Assert.NotNull(row);
            Assert.Equal(DataMovement.MetadataOnly, row.Movement);
            Assert.Equal(LockLevel.SchMBrief, row.Lock);
        }
    }

    [Fact]
    public void Notnull_default_const_splits_by_edition()
    {
        var enterprise = Catalog.Lookup(
            DdlOperationKeys.AddColumnNotNullDefaultConst, SqlServerVersion.Sql2022, SqlEdition.Enterprise);
        var express = Catalog.Lookup(
            DdlOperationKeys.AddColumnNotNullDefaultConst, SqlServerVersion.Sql2022, SqlEdition.Express);
        Assert.NotNull(enterprise);
        Assert.NotNull(express);
        Assert.Equal(DataMovement.MetadataOnly, enterprise.Movement);
        Assert.Equal(DataMovement.Rewrite, express.Movement);
    }

    [Fact]
    public void Offline_nonclustered_index_expects_reads_allowed_writes_blocked()
    {
        // The claim defended in docs/rules/MSSQL-LOCK-002.md: an offline nonclustered build
        // takes a table-level S lock — reads allowed, writes blocked.
        var row = Catalog.Lookup(
            DdlOperationKeys.CreateNonclusteredIndexOffline, SqlServerVersion.Sql2022, SqlEdition.Enterprise);
        Assert.NotNull(row);
        Assert.Equal(LockLevel.STable, row.Lock);
        Assert.Equal(
            new BlockingProfile(ReadsBlocked: false, WritesBlocked: true),
            VerdictEvaluator.ExpectedBlockingProfile(row.Lock));
    }
}
