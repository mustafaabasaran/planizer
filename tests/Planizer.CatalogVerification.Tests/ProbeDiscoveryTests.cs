using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.CatalogVerification.Tests;

/// <summary>Reflection discovery of probes; no server involved.</summary>
public sealed class ProbeDiscoveryTests
{
    [Fact]
    public void Discovery_is_type_name_ordered_and_includes_the_exemplar_probes()
    {
        // Probe files are contributed by parallel tasks, so this asserts the discovery
        // contract (deterministic type-name order) and the T1 exemplars rather than an exact
        // registry of every probe.
        var probes = ProbeRunner.DiscoverProbes();
        var typeNames = probes.Select(p => p.GetType().FullName!).ToList();
        Assert.Equal(typeNames.OrderBy(n => n, StringComparer.Ordinal), typeNames);

        var keys = probes.Select(p => p.OperationKey).ToHashSet(StringComparer.Ordinal);
        Assert.Superset(
            new HashSet<string>(StringComparer.Ordinal)
            {
                DdlOperationKeys.AddColumnNotNullDefaultConst,
                DdlOperationKeys.AddColumnNullable,
                DdlOperationKeys.CreateNonclusteredIndexOffline,
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
    public void Every_exemplar_probe_applies_to_both_ci_editions()
    {
        foreach (var probe in ProbeRunner.DiscoverProbes())
        {
            Assert.True(probe.AppliesTo(SqlEdition.Enterprise), $"{probe.OperationKey} must run on Developer");
            Assert.True(probe.AppliesTo(SqlEdition.Express), $"{probe.OperationKey} must run on Express");
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
