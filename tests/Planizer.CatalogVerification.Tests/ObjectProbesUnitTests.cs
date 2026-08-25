using Planizer.CatalogVerification.Tests.Probes;
using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.CatalogVerification.Tests;

/// <summary>
/// Server-free consistency checks of the object probes (T4): every probe's OperationKey must
/// resolve to a catalog row on both CI editions, and every declared expectation must be one
/// the <see cref="VerdictEvaluator"/> can actually judge for that row — a probe that declared
/// an unjudgeable aspect could never be Verified and would report Inconclusive forever.
/// </summary>
public sealed class ObjectProbesUnitTests
{
    private static readonly DdlBehaviorCatalog Catalog = DdlBehaviorCatalog.Load();

    private static readonly SqlEdition[] CiEditions = [SqlEdition.Enterprise, SqlEdition.Express];

    private static IReadOnlyList<ICatalogProbe> ObjectProbes() =>
    [
        new AddCheckOrFkProbe(),
        new DropTableProbe(),
        new TruncateTableProbe(),
        new SpRenameProbe(),
        new AlterTableSwitchProbe(),
        new EnableDisableTriggerProbe(),
    ];

    private static DdlBehavior LookupRequired(string operationKey, SqlEdition edition)
    {
        var row = Catalog.Lookup(operationKey, SqlServerVersion.Sql2022, edition);
        Assert.True(row is not null, $"no catalog row for '{operationKey}' on {edition}/Sql2022");
        return row!;
    }

    [Fact]
    public void Every_object_probe_key_resolves_to_a_catalog_row_on_both_ci_editions()
    {
        foreach (var probe in ObjectProbes())
        {
            foreach (var edition in CiEditions)
            {
                LookupRequired(probe.OperationKey, edition);
            }
        }
    }

    [Fact]
    public void Every_object_probe_applies_to_both_ci_editions()
    {
        // All six catalog rows are edition-independent ("any"), so every probe must run on
        // both PIDs of the CI matrix.
        foreach (var probe in ObjectProbes())
        {
            Assert.True(probe.AppliesTo(SqlEdition.Enterprise), $"{probe.OperationKey} must run on Developer");
            Assert.True(probe.AppliesTo(SqlEdition.Express), $"{probe.OperationKey} must run on Express");
        }
    }

    [Fact]
    public void Every_object_probe_declares_at_least_one_aspect()
    {
        foreach (var probe in ObjectProbes())
        {
            Assert.NotEqual(ProbeAspects.None, probe.Expectation.Aspects);
        }
    }

    [Fact]
    public void Movement_probes_target_rows_the_log_byte_classifier_can_judge()
    {
        // The evaluator can only confirm metadata_only (and rewrite) from a log-byte delta;
        // every probe declaring Movement must therefore sit on a metadata_only row, on both
        // editions, or it could never be Verified.
        foreach (var probe in ObjectProbes().Where(p => p.Expectation.Aspects.HasFlag(ProbeAspects.Movement)))
        {
            foreach (var edition in CiEditions)
            {
                var row = LookupRequired(probe.OperationKey, edition);
                Assert.Equal(DataMovement.MetadataOnly, row.Movement);
            }
        }
    }

    [Fact]
    public void Blocking_probes_target_schm_rows_expecting_full_blocking()
    {
        // add_check_or_fk, drop_table and truncate_table are all cataloged as schm: their
        // held-open DDL must block reads and writes alike, consistent with the lock semantics
        // of docs/rules/MSSQL-LOCK-002.md / MSSQL-LOCK-004.md.
        foreach (var probe in ObjectProbes().Where(p => p.Expectation.Aspects.HasFlag(ProbeAspects.Blocking)))
        {
            foreach (var edition in CiEditions)
            {
                var row = LookupRequired(probe.OperationKey, edition);
                Assert.Equal(LockLevel.SchM, row.Lock);
                Assert.Equal(
                    new BlockingProfile(ReadsBlocked: true, WritesBlocked: true),
                    VerdictEvaluator.ExpectedBlockingProfile(row.Lock));
            }
        }
    }

    [Fact]
    public void Truncate_probe_expects_the_foreign_key_reference_error_the_plan_fixes()
    {
        var expectation = new TruncateTableProbe().Expectation;
        Assert.True(expectation.Aspects.HasFlag(ProbeAspects.Error));
        Assert.Equal(4712, expectation.ErrorNumber);
    }

    [Fact]
    public void Unjudgeable_movement_classes_are_checked_internally_not_declared()
    {
        // full_scan, none and deallocate cannot be confirmed from log bytes; the probes on
        // those rows keep the claims as internal throw-on-violation checks (→ Inconclusive)
        // instead of declaring Movement, which would make every run Inconclusive by design.
        var unjudgeable = new (ICatalogProbe Probe, DataMovement CatalogClass)[]
        {
            (new AddCheckOrFkProbe(), DataMovement.FullScan),
            (new DropTableProbe(), DataMovement.None),
            (new TruncateTableProbe(), DataMovement.Deallocate),
        };
        foreach (var (probe, catalogClass) in unjudgeable)
        {
            Assert.False(
                probe.Expectation.Aspects.HasFlag(ProbeAspects.Movement),
                $"{probe.OperationKey} must not declare Movement");
            foreach (var edition in CiEditions)
            {
                Assert.Equal(catalogClass, LookupRequired(probe.OperationKey, edition).Movement);
            }
        }
    }

    [Fact]
    public void Object_probe_auxiliary_names_stay_unique_per_probe()
    {
        // Every probe owns probe_<key>; the auxiliary objects (child/target tables, procedure,
        // trigger) are suffixed off that prefix, so no two probes can ever collide.
        var keys = ObjectProbes().Select(p => p.OperationKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }
}
