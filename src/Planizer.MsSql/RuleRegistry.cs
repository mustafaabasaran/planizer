using Planizer.MsSql.Rules.Dynamic;
using Planizer.MsSql.Rules.Failure;
using Planizer.MsSql.Rules.Hygiene;
using Planizer.MsSql.Rules.Locking;
using Planizer.MsSql.Rules.Reversibility;
using Planizer.MsSql.Rules.Rewrite;

namespace Planizer.MsSql;

/// <summary>
/// Explicit list of every rule in this assembly. Reflection discovery
/// (<c>Assembly.GetTypes()</c> + <c>Activator.CreateInstance</c>) is not used at runtime because
/// Native AOT trimming removes types that are never referenced — the first AOT build shipped with
/// 1 of 52 rules. <c>RuleRegistryTests</c> compares this list against reflection, so a rule class
/// added without a line here fails the test suite.
/// </summary>
public static class RuleRegistry
{
    /// <summary>Fresh instances of all rules, in no particular order.</summary>
    public static IReadOnlyList<MsSqlRuleBase> CreateAll() =>
    [
        // Dynamic
        new DynamicSqlRule(),

        // Failure risk
        new IdentifierLengthRule(),
        new IndexKeyLimitRule(),
        new NewColumnUsedInSameBatchRule(),
        new NonUnicodeLiteralRule(),
        new UnguardedAlterTableRule(),
        new UnguardedCreateRule(),
        new UnguardedDropRule(),
        new UnsupportedFeatureRule(),
        new VariableAcrossBatchRule(),

        // Hygiene
        new EnvCrossDatabaseReferenceRule(),
        new EnvProgressMessageRule(),
        new EnvUseDatabaseRule(),
        new SetNoCountRule(),
        new SetQuotedIdentifierAnsiNullsRule(),
        new TranCatchSwallowsErrorRule(),
        new TranCatchWithoutRollbackRule(),
        new TranLongTransactionRule(),
        new TranMissingXactAbortRule(),
        new TranSpansBatchesRule(),
        new TranUnbalancedTransactionRule(),

        // Locking
        new IndexRebuildOfflineRule(),
        new MissingLockTimeoutRule(),
        new MultipleSchMInTransactionRule(),
        new OfflineIndexBuildLockRule(),
        new OnlineIndexEditionRule(),
        new OnlineIndexWaitAtLowPriorityRule(),
        new ResumableIndexRule(),
        new SchMDeadlockPotentialRule(),
        new SchemaModificationLockRule(),
        new UnboundedUpdateDeleteRule(),

        // Reversibility
        new IdentityInsertLeftOnRule(),
        new IrreversibleStatementRule(),
        new MissingRollbackRule(),
        new SpRenameDependencyRule(),
        new TruncateTableRule(),

        // Rewrite vs metadata-only
        new AddNotNullColumnWithDefaultRule(),
        new AddNotNullColumnWithoutDefaultRule(),
        new AddNullableColumnRule(),
        new AlterColumnCollationChangeRule(),
        new AlterColumnNarrowingRule(),
        new AlterColumnNotNullToNullRule(),
        new AlterColumnNullToNotNullRule(),
        new AlterColumnToMaxRule(),
        new AlterColumnTypeChangeRule(),
        new CheckOrForeignKeyConstraintRule(),
        new ClusteredIndexRewriteRule(),
        new DataCompressionChangeRule(),
        new DropColumnRule(),
        new PersistedComputedColumnRule(),
        new PrimaryKeyOrUniqueConstraintRule(),
        new RowWidthRule(),
    ];
}
