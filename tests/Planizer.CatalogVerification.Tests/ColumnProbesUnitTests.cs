using Planizer.Core;
using Planizer.CatalogVerification.Tests.Probes;
using Planizer.MsSql;

namespace Planizer.CatalogVerification.Tests;

/// <summary>
/// Server-free consistency checks for the column probes (plan task T2): every probe's
/// OperationKey resolves in the catalog for both CI matrix editions, and each probe's declared
/// expectation is one the verdict evaluator can actually judge against its catalog row — so a
/// CI run can only ever end Verified or Contradicted for reasons of measurement, never because
/// a probe was wired to an unjudgeable aspect.
/// </summary>
public sealed class ColumnProbesUnitTests
{
    private static readonly DdlBehaviorCatalog Catalog = DdlBehaviorCatalog.Load();

    private static readonly SqlEdition[] CiEditions = [SqlEdition.Enterprise, SqlEdition.Express];

    /// <summary>The catalog rows plan task T2 assigns to the column probe file.</summary>
    private static readonly string[] ColumnOperationKeys =
    [
        DdlOperationKeys.AddColumnNullable,
        DdlOperationKeys.AddColumnNotNullDefaultConst,
        DdlOperationKeys.AddColumnNotNullDefaultNondet,
        DdlOperationKeys.AddColumnNotNullNoDefault,
        DdlOperationKeys.AlterColumnWidenVarLen,
        DdlOperationKeys.AlterColumnWidenToMax,
        DdlOperationKeys.AlterColumnFixedLenChange,
        DdlOperationKeys.AlterColumnNarrow,
        DdlOperationKeys.AlterColumnNullToNotNull,
        DdlOperationKeys.AlterColumnNotNullToNull,
        DdlOperationKeys.AlterColumnCollation,
        DdlOperationKeys.DropColumn,
        DdlOperationKeys.AddComputedPersisted,
        DdlOperationKeys.DataCompressionChange,
        DdlOperationKeys.AddDefaultConstraint,
    ];

    private static IReadOnlyList<ICatalogProbe> ColumnProbes() =>
        ProbeRunner.DiscoverProbes()
            .Where(p => ColumnOperationKeys.Contains(p.OperationKey, StringComparer.Ordinal))
            .ToList();

    [Fact]
    public void Every_column_operation_key_has_exactly_one_probe()
    {
        var keys = ColumnProbes().Select(p => p.OperationKey).ToList();
        Assert.Equal(
            ColumnOperationKeys.OrderBy(k => k, StringComparer.Ordinal),
            keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_column_probe_resolves_a_catalog_row_on_both_ci_editions()
    {
        foreach (var probe in ColumnProbes())
        {
            foreach (var edition in CiEditions)
            {
                var row = Catalog.Lookup(probe.OperationKey, SqlServerVersion.Sql2022, edition);
                Assert.True(row is not null, $"no catalog row for '{probe.OperationKey}' on {edition}/Sql2022");
            }
        }
    }

    [Fact]
    public void Every_column_probe_applies_to_both_ci_editions()
    {
        foreach (var probe in ColumnProbes())
        {
            foreach (var edition in CiEditions)
            {
                Assert.True(probe.AppliesTo(edition), $"{probe.OperationKey} must run on {edition}");
            }
        }
    }

    [Fact]
    public void Movement_probes_target_rows_the_log_classifier_can_judge()
    {
        // The evaluator turns a log delta into a verdict only for metadata_only and rewrite
        // rows; any other movement class would leave the probe permanently Inconclusive.
        foreach (var probe in ColumnProbes().Where(p => p.Expectation.Aspects.HasFlag(ProbeAspects.Movement)))
        {
            foreach (var edition in CiEditions)
            {
                var row = Catalog.Lookup(probe.OperationKey, SqlServerVersion.Sql2022, edition);
                Assert.NotNull(row);
                Assert.True(
                    row.Movement is DataMovement.MetadataOnly or DataMovement.Rewrite,
                    $"'{probe.OperationKey}' on {edition} declares Movement but its catalog class " +
                    $"'{row.Movement}' cannot be judged from log bytes");
            }
        }
    }

    [Fact]
    public void Blocking_probes_cover_the_full_scan_rows_and_expect_full_blocking()
    {
        // The two full_scan column rows verify their lock column instead of movement: both are
        // cataloged Sch-M, which must block reads and writes while the DDL is held open.
        var blockingProbes = ColumnProbes()
            .Where(p => p.Expectation.Aspects.HasFlag(ProbeAspects.Blocking))
            .ToList();
        Assert.Equal(
            new[] { DdlOperationKeys.AddComputedPersisted, DdlOperationKeys.AlterColumnNullToNotNull },
            blockingProbes.Select(p => p.OperationKey).OrderBy(k => k, StringComparer.Ordinal));

        foreach (var probe in blockingProbes)
        {
            foreach (var edition in CiEditions)
            {
                var row = Catalog.Lookup(probe.OperationKey, SqlServerVersion.Sql2022, edition);
                Assert.NotNull(row);
                Assert.Equal(DataMovement.FullScan, row.Movement);
                Assert.Equal(LockLevel.SchM, row.Lock);
                Assert.Equal(
                    new BlockingProfile(ReadsBlocked: true, WritesBlocked: true),
                    VerdictEvaluator.ExpectedBlockingProfile(row.Lock));
            }
        }
    }

    [Fact]
    public void The_only_error_probe_expects_4901_on_a_fails_if_rows_row()
    {
        var errorProbes = ColumnProbes()
            .Where(p => p.Expectation.Aspects.HasFlag(ProbeAspects.Error))
            .ToList();
        var probe = Assert.Single(errorProbes);
        Assert.Equal(DdlOperationKeys.AddColumnNotNullNoDefault, probe.OperationKey);
        Assert.Equal(AddColumnNotNullNoDefaultProbe.AlterTableAddRequiresDefaultErrorNumber, probe.Expectation.ErrorNumber);
        Assert.Equal(4901, probe.Expectation.ErrorNumber);

        foreach (var edition in CiEditions)
        {
            var row = Catalog.Lookup(probe.OperationKey, SqlServerVersion.Sql2022, edition);
            Assert.NotNull(row);
            Assert.Equal(DataMovement.FailsIfRows, row.Movement);
        }
    }

    [Fact]
    public void Notnull_default_nondet_is_a_rewrite_on_both_editions()
    {
        // The catalog claim behind the probe: a per-row default breaks the metadata-only fast
        // path on every edition, unlike the runtime-constant default which splits by edition.
        foreach (var edition in CiEditions)
        {
            var row = Catalog.Lookup(
                DdlOperationKeys.AddColumnNotNullDefaultNondet, SqlServerVersion.Sql2022, edition);
            Assert.NotNull(row);
            Assert.Equal(DataMovement.Rewrite, row.Movement);
        }
    }

    [Fact]
    public void Every_probe_uses_a_unique_probe_prefixed_table_name()
    {
        // Table names derive from operation keys, so uniqueness across the whole discovered set
        // guarantees parallel-authored probe files can never collide on server objects.
        var tableNames = ProbeRunner.DiscoverProbes()
            .Select(p => ProbeSql.TableNameFor(p.OperationKey))
            .ToList();
        Assert.Equal(tableNames.Count, tableNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(tableNames, name => Assert.StartsWith("probe_", name, StringComparison.Ordinal));
    }
}
