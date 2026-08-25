using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

public class CatalogTests
{
    private static readonly DdlBehaviorCatalog Catalog = DdlBehaviorCatalog.Load();

    [Fact]
    public void Edition_specific_rows_win_over_any_and_each_other()
    {
        var enterprise = Catalog.Lookup(
            DdlOperationKeys.AddColumnNotNullDefaultConst, SqlServerVersion.Sql2019, SqlEdition.Enterprise);
        var standard = Catalog.Lookup(
            DdlOperationKeys.AddColumnNotNullDefaultConst, SqlServerVersion.Sql2019, SqlEdition.Standard);

        Assert.NotNull(enterprise);
        Assert.Equal(DataMovement.MetadataOnly, enterprise.Movement);
        Assert.Equal(LockLevel.SchMBrief, enterprise.Lock);

        Assert.NotNull(standard);
        Assert.Equal(DataMovement.Rewrite, standard.Movement);
        Assert.Equal(LockLevel.SchM, standard.Lock);
    }

    [Fact]
    public void Version_gated_row_does_not_apply_below_its_min_version()
    {
        // Enterprise row demands a different edition; the standard_express row demands 2016sp1.
        var behavior = Catalog.Lookup(
            DdlOperationKeys.DataCompressionChange, SqlServerVersion.Sql2014, SqlEdition.Standard);

        Assert.Null(behavior);
    }

    [Fact]
    public void Version_gated_row_applies_at_or_above_its_min_version()
    {
        var behavior = Catalog.Lookup(
            DdlOperationKeys.DataCompressionChange, SqlServerVersion.Sql2019, SqlEdition.Standard);

        Assert.NotNull(behavior);
        Assert.Equal(DataMovement.Rewrite, behavior.Movement);
    }

    [Fact]
    public void Plain_2016_does_not_satisfy_a_2016sp1_row()
    {
        // Patch level is unknown offline, so a bare "2016" target must stay conservative.
        var behavior = Catalog.Lookup(
            DdlOperationKeys.DataCompressionChange, SqlServerVersion.Sql2016, SqlEdition.Standard);

        Assert.Null(behavior);
    }

    [Fact]
    public void Unknown_operation_key_returns_null()
    {
        Assert.Null(Catalog.Lookup("no_such_operation", SqlServerVersion.Sql2019, SqlEdition.Standard));
    }

    [Theory]
    [InlineData(SqlEdition.Azure)] // Azure behaves like Enterprise
    [InlineData(SqlEdition.Enterprise)]
    public void Azure_maps_to_enterprise_rows(SqlEdition edition)
    {
        var behavior = Catalog.Lookup(
            DdlOperationKeys.CreateClusteredIndexOnline, SqlServerVersion.AzureSql, edition);

        Assert.NotNull(behavior);
        Assert.Equal(LockLevel.SchMBrief, behavior.Lock);
    }

    [Fact]
    public void Express_maps_to_standard_express_rows()
    {
        var behavior = Catalog.Lookup(
            DdlOperationKeys.AddColumnNotNullDefaultConst, SqlServerVersion.Sql2019, SqlEdition.Express);

        Assert.NotNull(behavior);
        Assert.Equal(DataMovement.Rewrite, behavior.Movement);
    }

    [Fact]
    public void Enterprise_only_rows_have_no_standard_fallback()
    {
        Assert.Null(Catalog.Lookup(
            DdlOperationKeys.CreateClusteredIndexOnline, SqlServerVersion.Sql2019, SqlEdition.Standard));
        Assert.Null(Catalog.Lookup(
            DdlOperationKeys.CreateNonclusteredIndexOnline, SqlServerVersion.Sql2019, SqlEdition.Standard));
        Assert.Null(Catalog.Lookup(DdlOperationKeys.AlterIndexRebuildOnline, SqlServerVersion.Sql2022, SqlEdition.Express));
    }

    [Fact]
    public void Reversibility_and_notes_come_through()
    {
        var spRename = Catalog.Lookup(DdlOperationKeys.SpRename, SqlServerVersion.Sql2019, SqlEdition.Standard);
        var dropTable = Catalog.Lookup(DdlOperationKeys.DropTable, SqlServerVersion.Sql2019, SqlEdition.Standard);
        var addNullable = Catalog.Lookup(DdlOperationKeys.AddColumnNullable, SqlServerVersion.Sql2019, SqlEdition.Standard);

        Assert.NotNull(spRename);
        Assert.Equal(Reversibility.WithScript, spRename.Reversible);
        Assert.Equal("dependencies not updated (deferred name resolution)", spRename.Notes);

        Assert.NotNull(dropTable);
        Assert.Equal(Reversibility.No, dropTable.Reversible);
        Assert.Null(dropTable.Notes); // empty CSV notes field becomes null

        Assert.NotNull(addNullable);
        Assert.Equal(Reversibility.Yes, addNullable.Reversible);
    }

    [Fact]
    public void Every_declared_operation_key_has_a_csv_row_and_vice_versa()
    {
        Assert.Equal(
            DdlOperationKeys.All.OrderBy(k => k, StringComparer.Ordinal),
            Catalog.OperationKeys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_key_resolves_for_every_edition_and_version_or_is_a_known_gap()
    {
        // The only intentional gaps: enterprise-only online operations on Standard/Express,
        // and data compression on Standard/Express before 2016 SP1.
        SqlServerVersion[] versions =
            [SqlServerVersion.Sql2014, SqlServerVersion.Sql2016, SqlServerVersion.Sql2017,
             SqlServerVersion.Sql2019, SqlServerVersion.Sql2022, SqlServerVersion.AzureSql];

        foreach (var key in DdlOperationKeys.All)
        {
            foreach (var version in versions)
            {
                Assert.NotNull(Catalog.Lookup(key, version, SqlEdition.Enterprise));

                var standard = Catalog.Lookup(key, version, SqlEdition.Standard);
                var isKnownGap =
                    key is DdlOperationKeys.CreateClusteredIndexOnline
                        or DdlOperationKeys.CreateNonclusteredIndexOnline
                        or DdlOperationKeys.AlterIndexRebuildOnline
                    || (key is DdlOperationKeys.DataCompressionChange && version <= SqlServerVersion.Sql2016);
                Assert.True(isKnownGap == (standard is null),
                    $"Unexpected lookup result for {key} / {version} / Standard.");
            }
        }
    }
}
