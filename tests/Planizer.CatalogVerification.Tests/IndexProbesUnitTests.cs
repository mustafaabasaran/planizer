using Planizer.CatalogVerification.Tests.Probes;
using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.CatalogVerification.Tests;

/// <summary>
/// Server-free consistency checks of the index probes: every probe's operation key must
/// resolve in the catalog for each CI edition it applies to, and each expectation must agree
/// with what the catalog row (through <see cref="VerdictEvaluator.ExpectedBlockingProfile"/>,
/// i.e. the verified lock semantics of <c>docs/rules/MSSQL-LOCK-002.md</c> and
/// <c>MSSQL-LOCK-004.md</c>) implies.
/// </summary>
public sealed class IndexProbesUnitTests
{
    private static readonly DdlBehaviorCatalog Catalog = DdlBehaviorCatalog.Load();

    private static readonly SqlEdition[] CiEditions = [SqlEdition.Enterprise, SqlEdition.Express];

    private static readonly IReadOnlyList<ICatalogProbe> IndexProbes =
    [
        new CreateNonclusteredIndexOfflineProbe(),
        new CreateNonclusteredIndexOnlineProbe(),
        new CreateClusteredIndexOnHeapProbe(),
        new CreateClusteredIndexOnlineProbe(),
        new DropClusteredIndexProbe(),
        new AlterIndexRebuildOfflineProbe(),
        new AlterIndexRebuildOnlineProbe(),
        new AlterIndexReorganizeProbe(),
        new AddPkOrUniqueProbe(),
    ];

    private static readonly IReadOnlySet<string> EnterpriseOnlyKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        DdlOperationKeys.CreateNonclusteredIndexOnline,
        DdlOperationKeys.CreateClusteredIndexOnline,
        DdlOperationKeys.AlterIndexRebuildOnline,
    };

    /// <summary>
    /// The blocking profile each probe's held-open measurement must produce, keyed by
    /// operation key. Derived from the catalog lock levels and pinned here so a catalog edit
    /// that silently changes what a probe asserts fails a unit test first.
    /// </summary>
    /// <summary>Probes that sample while the DDL runs instead of holding the transaction open.</summary>
    private static readonly IReadOnlySet<string> ConcurrentlySampledKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        DdlOperationKeys.CreateNonclusteredIndexOffline,
        DdlOperationKeys.CreateNonclusteredIndexOnline,
        DdlOperationKeys.AlterIndexReorganize,
    };

    private static readonly IReadOnlyDictionary<string, BlockingProfile> ExpectedProfiles =
        new Dictionary<string, BlockingProfile>(StringComparer.Ordinal)
        {
            // s_table: reads allowed, writes blocked (MSSQL-LOCK-002).
            [DdlOperationKeys.CreateNonclusteredIndexOffline] = new(ReadsBlocked: false, WritesBlocked: true),
            // s_brief, sampled during the build: online means concurrent DML — nothing blocked
            // mid-build; the brief S locks live only in the preparation/final instants.
            [DdlOperationKeys.CreateNonclusteredIndexOnline] = new(ReadsBlocked: false, WritesBlocked: false),
            // schm: all access blocked.
            [DdlOperationKeys.CreateClusteredIndexOnHeap] = new(ReadsBlocked: true, WritesBlocked: true),
            [DdlOperationKeys.DropClusteredIndex] = new(ReadsBlocked: true, WritesBlocked: true),
            [DdlOperationKeys.AlterIndexRebuildOffline] = new(ReadsBlocked: true, WritesBlocked: true),
            [DdlOperationKeys.AddPkOrUnique] = new(ReadsBlocked: true, WritesBlocked: true),
            // schm_brief: online clustered create and every online rebuild finish on a Sch-M;
            // held open, the category (not the duration) is what the probe verifies.
            [DdlOperationKeys.CreateClusteredIndexOnline] = new(ReadsBlocked: true, WritesBlocked: true),
            [DdlOperationKeys.AlterIndexRebuildOnline] = new(ReadsBlocked: true, WritesBlocked: true),
            // none: reorganize is always online.
            [DdlOperationKeys.AlterIndexReorganize] = new(ReadsBlocked: false, WritesBlocked: false),
        };

    [Fact]
    public void Every_index_probe_key_resolves_for_each_ci_edition_it_applies_to()
    {
        foreach (var probe in IndexProbes)
        {
            foreach (var edition in CiEditions)
            {
                if (!probe.AppliesTo(edition))
                {
                    continue;
                }

                var row = Catalog.Lookup(probe.OperationKey, SqlServerVersion.Sql2022, edition);
                Assert.True(row is not null, $"no catalog row for '{probe.OperationKey}' on {edition}/Sql2022");
            }
        }
    }

    [Fact]
    public void Exactly_the_enterprise_scoped_online_probes_are_edition_gated()
    {
        foreach (var probe in IndexProbes)
        {
            Assert.True(probe.AppliesTo(SqlEdition.Enterprise), $"{probe.OperationKey} must run on Developer");
            var expectedOnExpress = !EnterpriseOnlyKeys.Contains(probe.OperationKey);
            Assert.Equal(expectedOnExpress, probe.AppliesTo(SqlEdition.Express));
        }
    }

    [Fact]
    public void Every_blocking_expectation_matches_the_catalog_rows_lock_level()
    {
        foreach (var probe in IndexProbes)
        {
            Assert.True(probe.Expectation.Aspects.HasFlag(ProbeAspects.Blocking),
                $"{probe.OperationKey} is an index probe and must measure blocking");

            // Evaluate on an edition the probe runs on; Enterprise covers all index probes.
            var row = Catalog.Lookup(probe.OperationKey, SqlServerVersion.Sql2022, SqlEdition.Enterprise);
            Assert.NotNull(row);
            var expected = ConcurrentlySampledKeys.Contains(probe.OperationKey)
                ? VerdictEvaluator.DuringExecutionBlockingProfile(row.Lock)
                : VerdictEvaluator.ExpectedBlockingProfile(row.Lock);
            Assert.Equal(ExpectedProfiles[probe.OperationKey], expected);
        }
    }

    [Fact]
    public void Resumable_inside_explicit_transaction_expects_error_574_on_the_online_rebuild_probe()
    {
        var probe = new AlterIndexRebuildOnlineProbe();
        Assert.True(probe.Expectation.Aspects.HasFlag(ProbeAspects.Error));
        Assert.Equal(574, probe.Expectation.ErrorNumber);
        Assert.Equal(574, IndexProbeErrors.StatementNotAllowedInUserTransaction);
    }

    [Fact]
    public void Online_on_express_expects_error_1712()
    {
        // Asserted by the gated OnlineIndexEditionGateTests fact rather than a probe: on
        // Express the online rows deliberately resolve to no catalog row, so the runner has
        // nothing to compare — the 1712 failure is the empirical form of that absence.
        Assert.Equal(1712, IndexProbeErrors.OnlineOperationsRequireEnterprise);
        Assert.Null(Catalog.Lookup(
            DdlOperationKeys.CreateNonclusteredIndexOnline, SqlServerVersion.Sql2022, SqlEdition.Express));
        Assert.Null(Catalog.Lookup(
            DdlOperationKeys.CreateClusteredIndexOnline, SqlServerVersion.Sql2022, SqlEdition.Express));
        Assert.Null(Catalog.Lookup(
            DdlOperationKeys.AlterIndexRebuildOnline, SqlServerVersion.Sql2022, SqlEdition.Express));
    }

    [Fact]
    public void No_index_probe_declares_a_movement_expectation()
    {
        // The log-delta bands separate metadata_only from rewrite; they cannot judge the
        // index_build (or heap-rewrite) classes under an unknown recovery model, and the
        // evaluator would turn such a declaration into a permanent Inconclusive.
        foreach (var probe in IndexProbes)
        {
            Assert.False(probe.Expectation.Aspects.HasFlag(ProbeAspects.Movement),
                $"{probe.OperationKey} must not declare Movement");
        }
    }

    [Fact]
    public void Probe_table_names_are_unique_and_probe_prefixed()
    {
        var names = IndexProbes
            .Select(p => ProbeSql.TableNameFor(p.OperationKey))
            .Append(OnlineIndexEditionGateTests.TableName)
            .ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, name => Assert.StartsWith("probe_", name, StringComparison.Ordinal));
    }
}
