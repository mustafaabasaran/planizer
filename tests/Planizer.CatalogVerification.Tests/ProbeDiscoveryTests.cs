using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.CatalogVerification.Tests;

/// <summary>Reflection discovery of probes; no server involved.</summary>
public sealed class ProbeDiscoveryTests
{
    [Fact]
    public void Discovery_is_type_name_ordered_and_includes_the_registered_probes()
    {
        // All three probe families (columns, indexes, objects) are merged, so discovery must
        // yield exactly the full registry, in deterministic type-name order.
        var probes = ProbeRunner.DiscoverProbes();
        var typeNames = probes.Select(p => p.GetType().FullName!).ToList();
        Assert.Equal(typeNames.OrderBy(n => n, StringComparer.Ordinal), typeNames);

        var keys = probes.Select(p => p.OperationKey).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                DdlOperationKeys.AddCheckOrFk,
                DdlOperationKeys.AddColumnNotNullDefaultConst,
                DdlOperationKeys.AddColumnNotNullDefaultNondet,
                DdlOperationKeys.AddColumnNotNullNoDefault,
                DdlOperationKeys.AddColumnNullable,
                DdlOperationKeys.AddComputedPersisted,
                DdlOperationKeys.AddDefaultConstraint,
                DdlOperationKeys.AddPkOrUnique,
                DdlOperationKeys.AlterColumnCollation,
                DdlOperationKeys.AlterColumnFixedLenChange,
                DdlOperationKeys.AlterColumnNarrow,
                DdlOperationKeys.AlterColumnNotNullToNull,
                DdlOperationKeys.AlterColumnNullToNotNull,
                DdlOperationKeys.AlterColumnWidenToMax,
                DdlOperationKeys.AlterColumnWidenVarLen,
                DdlOperationKeys.AlterIndexRebuildOffline,
                DdlOperationKeys.AlterIndexRebuildOnline,
                DdlOperationKeys.AlterIndexReorganize,
                DdlOperationKeys.AlterTableSwitch,
                DdlOperationKeys.CreateClusteredIndexOnHeap,
                DdlOperationKeys.CreateClusteredIndexOnline,
                DdlOperationKeys.CreateNonclusteredIndexOffline,
                DdlOperationKeys.CreateNonclusteredIndexOnline,
                DdlOperationKeys.DataCompressionChange,
                DdlOperationKeys.DropClusteredIndex,
                DdlOperationKeys.DropColumn,
                DdlOperationKeys.DropTable,
                DdlOperationKeys.EnableDisableTrigger,
                DdlOperationKeys.SpRename,
                DdlOperationKeys.TruncateTable,
            },
            keys);
    }

    [Fact]
    public void Discovery_is_deterministic()
    {
        var first = ProbeRunner.DiscoverProbes().Select(p => p.GetType().FullName).ToList();
        var second = ProbeRunner.DiscoverProbes().Select(p => p.GetType().FullName).ToList();
        Assert.Equal(first, second);
    }

    [Fact]
    public void Every_probe_key_is_distinct()
    {
        var keys = ProbeRunner.DiscoverProbes().Select(p => p.OperationKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_probe_declares_at_least_one_aspect()
    {
        foreach (var probe in ProbeRunner.DiscoverProbes())
        {
            Assert.NotEqual(ProbeAspects.None, probe.Expectation.Aspects);
        }
    }

    [Fact]
    public void Every_probe_applies_to_at_least_one_ci_edition()
    {
        // Probes for enterprise-scoped catalog rows (the online index operations) run only on
        // the Developer leg of the CI matrix; every probe must still run somewhere.
        foreach (var probe in ProbeRunner.DiscoverProbes())
        {
            Assert.True(
                probe.AppliesTo(SqlEdition.Enterprise) || probe.AppliesTo(SqlEdition.Express),
                $"{probe.OperationKey} must run on at least one CI edition");
        }
    }

    [Fact]
    public void Error_expectations_carry_their_error_number()
    {
        foreach (var probe in ProbeRunner.DiscoverProbes())
        {
            if (probe.Expectation.Aspects.HasFlag(ProbeAspects.Error))
            {
                Assert.NotNull(probe.Expectation.ErrorNumber);
            }
        }
    }
}
