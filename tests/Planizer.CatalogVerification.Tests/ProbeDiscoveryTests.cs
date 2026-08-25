using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.CatalogVerification.Tests;

/// <summary>Reflection discovery of probes; no server involved.</summary>
public sealed class ProbeDiscoveryTests
{
    [Fact]
    public void The_exemplar_probes_are_discovered_in_type_name_order()
    {
        // The probe files added after T1 (columns/index/objects) extend the discovered list,
        // so this asserts containment of the exemplars plus the ordering invariant rather
        // than exact equality.
        var probes = ProbeRunner.DiscoverProbes();
        var keys = probes.Select(p => p.OperationKey).ToList();
        Assert.Contains(DdlOperationKeys.AddColumnNotNullDefaultConst, keys);
        Assert.Contains(DdlOperationKeys.AddColumnNullable, keys);
        Assert.Contains(DdlOperationKeys.CreateNonclusteredIndexOffline, keys);

        var typeNames = probes.Select(p => p.GetType().FullName).ToList();
        Assert.Equal(typeNames.OrderBy(name => name, StringComparer.Ordinal), typeNames);
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
