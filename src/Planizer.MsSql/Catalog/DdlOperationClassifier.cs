using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql;

/// <summary>
/// Maps a parsed statement to its <see cref="DdlOperationKeys"/> operation key so behavior can be
/// resolved from the <see cref="DdlBehaviorCatalog"/>. Offline and AST-only: statements whose key
/// depends on schema state that a script alone cannot reveal (a bare ALTER COLUMN type change —
/// widen vs narrow, DROP INDEX clustered vs not, …) map to <c>null</c> and callers must treat
/// them conservatively.
/// </summary>
public static class DdlOperationClassifier
{
    /// <summary>
    /// Precedence when one ALTER TABLE ADD carries several elements: the riskiest element names
    /// the operation (rows-error beats rewrite beats scans/builds beats metadata-only).
    /// </summary>
    private static readonly string[] AddElementPrecedence =
    [
        DdlOperationKeys.AddColumnNotNullNoDefault,
        DdlOperationKeys.AddColumnNotNullDefaultNondet,
        DdlOperationKeys.AddColumnNotNullDefaultConst,
        DdlOperationKeys.AddComputedPersisted,
        DdlOperationKeys.AddPkOrUnique,
        DdlOperationKeys.AddCheckOrFk,
        DdlOperationKeys.AddColumnNullable,
        DdlOperationKeys.AddDefaultConstraint,
    ];

    /// <summary>Operation key for a statement; <c>null</c> when the AST alone cannot determine one.</summary>
    public static string? GetOperationKey(TSqlStatement statement) => statement switch
    {
        AlterTableAddTableElementStatement add => ClassifyAdd(add),
        AlterTableAlterColumnStatement alterColumn => ClassifyAlterColumn(alterColumn),
        AlterTableDropTableElementStatement drop => ClassifyDropElement(drop),
        AlterTableSwitchStatement => DdlOperationKeys.AlterTableSwitch,
        AlterTableTriggerModificationStatement => DdlOperationKeys.EnableDisableTrigger,
        AlterTableRebuildStatement rebuild when HasDataCompressionOption(rebuild.IndexOptions)
            => DdlOperationKeys.DataCompressionChange,
        DropTableStatement => DdlOperationKeys.DropTable,
        TruncateTableStatement => DdlOperationKeys.TruncateTable,
        CreateIndexStatement create => ClassifyCreateIndex(create),
        AlterIndexStatement alter => ClassifyAlterIndex(alter),
        _ when StatementClassifier.IsProcedureCall(statement, "sp_rename") => DdlOperationKeys.SpRename,
        _ => null,
    };

    /// <summary>Behavior under the configured target; <c>null</c> when no key or no catalog row applies.</summary>
    public static DdlBehavior? GetBehavior(SqlStatementInfo statement, DdlBehaviorCatalog catalog, PlanizerConfig config)
        => GetOperationKey(statement.Ast) is { } key
            ? catalog.Lookup(key, config.TargetVersion, config.Edition)
            : null;

    /// <summary>
    /// Whether the statement acquires a schema-modification lock at all (full or brief) under the
    /// configured target — feeds <c>ScriptSummary.SchMLockCount</c>. An online index operation
    /// whose catalog row does not apply to the configured edition (ONLINE is Enterprise/Azure
    /// only) is judged by its offline-equivalent row, so the summary count does not silently
    /// shift with the edition. Statements the catalog still cannot resolve fall back to a
    /// conservative AST answer: every remaining ALTER TABLE variant and DROP INDEX take Sch-M;
    /// everything else does not. An online nonclustered create never counts: its brief shared (S)
    /// locks block writers, not all access.
    /// </summary>
    public static bool AcquiresSchMLock(SqlStatementInfo statement, DdlBehaviorCatalog catalog, PlanizerConfig config)
    {
        var behavior = GetBehavior(statement, catalog, config)
            ?? GetOfflineEquivalentBehavior(statement, catalog, config);

        return behavior is not null
            ? behavior.Lock is LockLevel.SchM or LockLevel.SchMBrief
            : statement.Ast is AlterTableStatement or DropIndexStatement;
    }

    /// <summary>
    /// Behavior of the offline form of an online index operation, used when the online catalog
    /// row does not apply to the configured edition (the statement itself would fail — LOCK-003).
    /// </summary>
    private static DdlBehavior? GetOfflineEquivalentBehavior(
        SqlStatementInfo statement, DdlBehaviorCatalog catalog, PlanizerConfig config)
    {
        var key = statement.Ast switch
        {
            CreateIndexStatement create when IsOnline(create.IndexOptions)
                => create.Clustered == true
                    ? DdlOperationKeys.CreateClusteredIndexOnHeap
                    : DdlOperationKeys.CreateNonclusteredIndexOffline,
            AlterIndexStatement { AlterIndexType: AlterIndexType.Rebuild } alter
                when IsOnline(alter.IndexOptions)
                => DdlOperationKeys.AlterIndexRebuildOffline,
            _ => null,
        };

        return key is null ? null : catalog.Lookup(key, config.TargetVersion, config.Edition);
    }

    private static string? ClassifyAdd(AlterTableAddTableElementStatement add)
    {
        var candidates = new List<string>();

        foreach (var column in add.Definition?.ColumnDefinitions ?? [])
        {
            var key = ClassifyAddedColumn(column);
            if (key is null)
            {
                return null; // one unclassifiable column makes the whole statement unknown
            }

            candidates.Add(key);
        }

        foreach (var constraint in add.Definition?.TableConstraints ?? [])
        {
            switch (constraint)
            {
                case UniqueConstraintDefinition: // PRIMARY KEY and UNIQUE alike
                    candidates.Add(DdlOperationKeys.AddPkOrUnique);
                    break;
                case CheckConstraintDefinition or ForeignKeyConstraintDefinition:
                    candidates.Add(DdlOperationKeys.AddCheckOrFk);
                    break;
                case DefaultConstraintDefinition:
                    // A default for an existing column (ADD [CONSTRAINT DF_x] DEFAULT 0 FOR C):
                    // existing rows are not touched, metadata-only.
                    candidates.Add(DdlOperationKeys.AddDefaultConstraint);
                    break;
                default:
                    return null;
            }
        }

        return candidates.Count == 0
            ? null
            : AddElementPrecedence.FirstOrDefault(candidates.Contains);
    }

    private static string? ClassifyAddedColumn(ColumnDefinition column)
    {
        if (column.IdentityOptions is not null)
        {
            return null; // adding an IDENTITY column has its own semantics; do not guess
        }

        if (column.ComputedColumnExpression is not null)
        {
            return column.IsPersisted
                ? DdlOperationKeys.AddComputedPersisted
                : DdlOperationKeys.AddColumnNullable; // non-persisted computed column is metadata-only
        }

        var notNull = column.Constraints.OfType<NullableConstraintDefinition>()
            .Any(c => !c.Nullable);
        if (!notNull)
        {
            return DdlOperationKeys.AddColumnNullable;
        }

        var defaultConstraint = column.DefaultConstraint
            ?? column.Constraints.OfType<DefaultConstraintDefinition>().FirstOrDefault();

        if (defaultConstraint?.Expression is not { } expression)
        {
            return DdlOperationKeys.AddColumnNotNullNoDefault;
        }

        return DefaultExpressionClassifier.IsRuntimeConstant(expression)
            ? DdlOperationKeys.AddColumnNotNullDefaultConst
            : DdlOperationKeys.AddColumnNotNullDefaultNondet;
    }

    /// <summary>
    /// Maps ALTER COLUMN through the offline-certain <see cref="Rules.Rewrite.AlterColumnClassifier"/>
    /// kinds; the riskiest certain change names the operation. A bare type respecification is the
    /// only genuinely ambiguous case offline (widen vs narrow needs the current type) → <c>null</c>.
    /// </summary>
    private static string? ClassifyAlterColumn(AlterTableAlterColumnStatement alter)
    {
        if (Rules.Rewrite.AlterColumnClassifier.Classify(alter) is not { } facts)
        {
            return null;
        }

        if (facts.Has(Rules.Rewrite.AlterColumnChangeKind.Collation))
        {
            return DdlOperationKeys.AlterColumnCollation;
        }

        if (facts.Has(Rules.Rewrite.AlterColumnChangeKind.WidenToMax))
        {
            return DdlOperationKeys.AlterColumnWidenToMax;
        }

        if (facts.Has(Rules.Rewrite.AlterColumnChangeKind.NullToNotNull))
        {
            return DdlOperationKeys.AlterColumnNullToNotNull;
        }

        if (facts.Has(Rules.Rewrite.AlterColumnChangeKind.NotNullToNull))
        {
            return DdlOperationKeys.AlterColumnNotNullToNull;
        }

        return null;
    }

    private static string? ClassifyDropElement(AlterTableDropTableElementStatement drop)
    {
        if (drop.AlterTableDropTableElements.Count == 0)
        {
            return null;
        }

        // In "DROP COLUMN A, B" only the first element carries the COLUMN keyword;
        // the rest are NotSpecified and inherit the preceding element's kind.
        var inherited = TableElementType.NotSpecified;
        foreach (var element in drop.AlterTableDropTableElements)
        {
            var type = element.TableElementType == TableElementType.NotSpecified
                ? inherited
                : element.TableElementType;

            if (type != TableElementType.Column)
            {
                return null; // DROP CONSTRAINT / mixed lists have no catalog row yet
            }

            inherited = type;
        }

        return DdlOperationKeys.DropColumn;
    }

    /// <summary>
    /// Clustered and nonclustered creates get separate keys in both the online and the offline
    /// path: an online <c>CLUSTERED</c> create ends with a schema-modification (Sch-M) lock on
    /// the table, while an online nonclustered create only ever takes a brief shared (S)
    /// lock to start and to complete — no blocking table Sch-M in any phase.
    /// </summary>
    private static string ClassifyCreateIndex(CreateIndexStatement create)
    {
        if (IsOnline(create.IndexOptions))
        {
            return create.Clustered == true
                ? DdlOperationKeys.CreateClusteredIndexOnline
                : DdlOperationKeys.CreateNonclusteredIndexOnline;
        }

        return create.Clustered == true
            ? DdlOperationKeys.CreateClusteredIndexOnHeap
            : DdlOperationKeys.CreateNonclusteredIndexOffline;
    }

    private static string? ClassifyAlterIndex(AlterIndexStatement alter) => alter.AlterIndexType switch
    {
        AlterIndexType.Rebuild when HasDataCompressionOption(alter.IndexOptions)
            => DdlOperationKeys.DataCompressionChange,
        AlterIndexType.Rebuild when IsOnline(alter.IndexOptions) => DdlOperationKeys.AlterIndexRebuildOnline,
        AlterIndexType.Rebuild => DdlOperationKeys.AlterIndexRebuildOffline,
        AlterIndexType.Reorganize => DdlOperationKeys.AlterIndexReorganize,
        _ => null,
    };

    private static bool IsOnline(IEnumerable<IndexOption> options)
        => options.OfType<IndexStateOption>()
            .Any(o => o.OptionKind == IndexOptionKind.Online && o.OptionState == OptionState.On);

    private static bool HasDataCompressionOption(IEnumerable<IndexOption> options)
        => options.OfType<DataCompressionOption>().Any();
}
