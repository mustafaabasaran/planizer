using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql.Parsing;

/// <summary>
/// How tightly the FROM clause of a WHERE-less, TOP-less UPDATE/DELETE bounds the write.
/// Ordered by strength: a smaller value is a stronger restriction, so combining two verdicts
/// along a join path is a minimum.
/// </summary>
public enum JoinBoundedness
{
    /// <summary>The statement provably touches a subset of the target's rows.</summary>
    Bounded,

    /// <summary>
    /// Whether the join restricts the target depends on the data — an INNER JOIN that happens to
    /// match every row bounds nothing. Offline this cannot be decided; the rule reports Info +
    /// <c>Inconclusive</c> rather than staying silent.
    /// </summary>
    Inconclusive,

    /// <summary>Nothing in the FROM clause restricts the target: every row is written.</summary>
    Unbounded,
}

/// <summary>
/// The verdict for one statement plus the join that decided it (<c>"LEFT JOIN"</c>,
/// <c>"CROSS APPLY"</c>, …), so a finding can name it. <c>null</c> when no join was involved.
/// </summary>
/// <param name="Boundedness">The verdict.</param>
/// <param name="Join">The deciding join, or <c>null</c> when the FROM clause holds no join.</param>
public readonly record struct JoinBounds(JoinBoundedness Boundedness, string? Join);

/// <summary>
/// Shape checks for DML statements shared by several rules: whether the target is a persistent
/// table at all, and whether a WHERE-less DELETE/UPDATE is still bounded by a JOIN.
/// </summary>
public static class DmlTargets
{
    /// <summary>
    /// Table variables (<c>@t</c>) and temp tables (<c>#t</c>, <c>##t</c>) are session-scoped:
    /// writing to them moves no persistent data and escalates no locks on user tables.
    /// </summary>
    public static bool IsTransient(TableReference? target) => target switch
    {
        VariableTableReference => true,
        NamedTableReference named => IsTransient(named.SchemaObject),
        _ => false,
    };

    public static bool IsTransient(SchemaObjectName? name)
        => name?.BaseIdentifier?.Value is { } n && n.StartsWith('#');

    /// <summary>
    /// Classifies a DELETE/UPDATE by how much of the target table it can touch. A WHERE clause,
    /// a TOP filter or a transient target make it <see cref="JoinBoundedness.Bounded"/> outright;
    /// otherwise the verdict comes from where the target sits in the join tree. A join can only
    /// filter when it can drop target rows: the preserved side of an outer join and a cross join
    /// never do (Unbounded); an inner join — and the null-supplying side of an outer join, which
    /// filters identically — drop rows only when the data says so (Inconclusive).
    /// </summary>
    public static JoinBounds ClassifyPersistentWrite(UpdateDeleteSpecificationBase? spec)
    {
        if (spec is null
            || spec.WhereClause is not null
            || spec.TopRowFilter is not null
            || IsTransientTarget(spec))
        {
            return new JoinBounds(JoinBoundedness.Bounded, null);
        }

        if (spec.FromClause?.TableReferences is not { Count: > 0 } refs)
        {
            return Unrestricted; // plain "DELETE FROM dbo.T;" — no FROM clause to bound it
        }

        // "UPDATE dbo.A SET … FROM dbo.B JOIN dbo.C": the target is not in the FROM clause at
        // all, so T-SQL cross joins it in and every row of it is written.
        if (ResolveTargetReference(spec) is not { } target)
        {
            return refs.Count > 1 ? new JoinBounds(JoinBoundedness.Unbounded, "comma cross join") : Unrestricted;
        }

        foreach (var reference in refs)
        {
            var (found, bounds) = Classify(reference, target);
            if (!found)
            {
                continue;
            }

            // Other comma-separated references cross join against the reference holding the
            // target: they multiply rows but can never resurrect the ones the target's own joins
            // dropped, so that reference's verdict stands. Only a BARE target in a multi-reference
            // list has no join of its own — that is the comma cross join itself.
            return refs.Count > 1 && bounds == Unrestricted
                ? new JoinBounds(JoinBoundedness.Unbounded, "comma cross join")
                : bounds;
        }

        return Unrestricted;
    }

    /// <summary>Unfiltered write to a persistent table: no WHERE, no TOP, no bounding JOIN, not transient.</summary>
    public static bool IsUnboundedPersistentWrite(UpdateDeleteSpecificationBase? spec)
        => ClassifyPersistentWrite(spec).Boundedness == JoinBoundedness.Unbounded;

    /// <summary>
    /// The table a DELETE/UPDATE actually writes to. <c>UPDATE T SET … FROM dbo.Orders T</c> names
    /// the alias as its target; the alias is resolved through the FROM clause so findings name
    /// <c>dbo.Orders</c>, not <c>T</c>. <c>null</c> for a table-variable or derived target.
    /// </summary>
    public static SchemaObjectName? ResolveTargetTable(UpdateDeleteSpecificationBase? spec)
    {
        if (spec?.Target is not NamedTableReference { SchemaObject: { } name })
        {
            return null;
        }

        if (name.Identifiers.Count != 1 || spec.FromClause is null)
        {
            return name;
        }

        return ResolveTargetReference(spec) is NamedTableReference { SchemaObject: { } resolved }
            ? resolved
            : name;
    }

    /// <summary>Nothing bounds the write, and no join is to blame for it.</summary>
    private static JoinBounds Unrestricted => new(JoinBoundedness.Unbounded, null);

    /// <summary>
    /// Whether the target — written directly or through an alias — is a table variable or temp
    /// table. The alias is resolved to the FROM-clause reference itself, so <c>DELETE i FROM @Ids i</c>
    /// counts as transient even though a table variable has no <see cref="SchemaObjectName"/>.
    /// </summary>
    private static bool IsTransientTarget(UpdateDeleteSpecificationBase? spec)
        => IsTransient(spec?.Target) || (spec is not null && IsTransient(ResolveTargetReference(spec)));

    /// <summary>
    /// The table reference inside the FROM clause that the statement writes to. An unqualified
    /// target name is an alias first (<c>UPDATE T … FROM dbo.Orders T</c>); failing that — and for
    /// a qualified target such as <c>DELETE FROM dbo.Orders FROM dbo.Orders o JOIN …</c> — the
    /// leaf is found by table name. <c>null</c> when the target does not appear in the FROM clause.
    /// </summary>
    private static TableReference? ResolveTargetReference(UpdateDeleteSpecificationBase spec)
    {
        if (spec.FromClause?.TableReferences is not { Count: > 0 } refs)
        {
            return null;
        }

        var leaves = refs.SelectMany(Flatten).ToList();

        if (spec.Target is VariableTableReference { Variable.Name: { } variable })
        {
            return leaves.FirstOrDefault(l => l is VariableTableReference { Variable.Name: { } v }
                                              && v.Equals(variable, StringComparison.OrdinalIgnoreCase));
        }

        if (spec.Target is not NamedTableReference { SchemaObject: { BaseIdentifier.Value: { } target } name })
        {
            return null;
        }

        // An unqualified target name is an alias first: "UPDATE T SET … FROM dbo.Orders T".
        if (name.Identifiers.Count == 1
            && leaves.FirstOrDefault(l => l is TableReferenceWithAlias { Alias.Value: { } alias }
                                          && alias.Equals(target, StringComparison.OrdinalIgnoreCase)) is { } aliased)
        {
            return aliased;
        }

        return leaves.FirstOrDefault(l => l is NamedTableReference { SchemaObject.BaseIdentifier.Value: { } name }
                                          && name.Equals(target, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Walks the join tree looking for <paramref name="target"/> and combines every join on the
    /// path from the root down to it: the strongest restriction wins, because one filtering join
    /// is enough to keep the write off the rest of the table.
    /// </summary>
    private static (bool Found, JoinBounds Bounds) Classify(TableReference node, TableReference target)
    {
        switch (node)
        {
            case JoinParenthesisTableReference parenthesis:
                return Classify(parenthesis.Join, target);

            case JoinTableReference join:
            {
                var left = Classify(join.FirstTableReference, target);
                if (left.Found)
                {
                    return (true, Strongest(Restriction(join, targetOnLeft: true), left.Bounds));
                }

                var right = Classify(join.SecondTableReference, target);
                return right.Found
                    ? (true, Strongest(Restriction(join, targetOnLeft: false), right.Bounds))
                    : (false, Unrestricted);
            }

            default:
                return (ReferenceEquals(node, target), Unrestricted);
        }
    }

    /// <summary>What one join node does to the rows of the side the target sits on.</summary>
    private static JoinBounds Restriction(JoinTableReference join, bool targetOnLeft) => join switch
    {
        // An inner join drops target rows without a match — but the ON predicate may match every
        // row, and offline there is no way to tell.
        QualifiedJoin { QualifiedJoinType: QualifiedJoinType.Inner }
            => new(JoinBoundedness.Inconclusive, "INNER JOIN"),

        // An outer join preserves its outer side in full. Its null-supplying side is filtered
        // exactly like an inner join — only rows with a match survive — and whether every row
        // matches is a data question, so it gets the same Inconclusive verdict as INNER JOIN.
        QualifiedJoin { QualifiedJoinType: QualifiedJoinType.LeftOuter }
            => new(targetOnLeft ? JoinBoundedness.Unbounded : JoinBoundedness.Inconclusive, "LEFT JOIN"),
        QualifiedJoin { QualifiedJoinType: QualifiedJoinType.RightOuter }
            => new(targetOnLeft ? JoinBoundedness.Inconclusive : JoinBoundedness.Unbounded, "RIGHT JOIN"),
        QualifiedJoin { QualifiedJoinType: QualifiedJoinType.FullOuter }
            => new(JoinBoundedness.Unbounded, "FULL OUTER JOIN"),

        // A cross join pairs every row of both sides: nothing is filtered out.
        UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.CrossJoin }
            => new(JoinBoundedness.Unbounded, "CROSS JOIN"),

        // OUTER APPLY keeps every left row even when the right side returns none; CROSS APPLY
        // drops those rows, but only if the right side can actually come back empty.
        UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.OuterApply }
            => new(targetOnLeft ? JoinBoundedness.Unbounded : JoinBoundedness.Inconclusive, "OUTER APPLY"),
        UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.CrossApply }
            => new(JoinBoundedness.Inconclusive, "CROSS APPLY"),

        _ => new(JoinBoundedness.Inconclusive, "join"),
    };

    /// <summary>
    /// The stronger of two verdicts along one join path. On a tie the inner (deeper) join wins the
    /// naming: it is the one written next to the target.
    /// </summary>
    private static JoinBounds Strongest(JoinBounds outer, JoinBounds inner)
    {
        if (inner.Boundedness < outer.Boundedness)
        {
            return inner;
        }

        if (outer.Boundedness < inner.Boundedness)
        {
            return outer;
        }

        return inner.Join is null ? outer : inner;
    }

    private static IEnumerable<TableReference> Flatten(TableReference reference) => reference switch
    {
        JoinTableReference join => Flatten(join.FirstTableReference).Concat(Flatten(join.SecondTableReference)),
        JoinParenthesisTableReference parenthesis => Flatten(parenthesis.Join),
        _ => new[] { reference },
    };

    /// <summary>
    /// True when the statement — DML or DDL — only touches session-scoped objects: temp tables
    /// (<c>#t</c>, <c>##t</c>) and table variables. Such statements move no persistent data, take
    /// no locks on user tables and need no rollback.
    /// </summary>
    public static bool TargetsTransientObject(TSqlStatement ast) => ast switch
    {
        InsertStatement or UpdateStatement or DeleteStatement or MergeStatement or SelectStatement
            => ast is not SelectStatement { Into: null } && !ModifiesPersistentData(ast),
        CreateTableStatement create => IsTransient(create.SchemaObjectName),
        AlterTableStatement alter => IsTransient(alter.SchemaObjectName),
        DropTableStatement drop => drop.Objects.Count > 0 && drop.Objects.All(IsTransient),
        TruncateTableStatement truncate => IsTransient(truncate.TableName),
        CreateIndexStatement index => IsTransient(index.OnName),
        DropIndexStatement dropIndex => dropIndex.DropIndexClauses.Count > 0
            && dropIndex.DropIndexClauses.All(c => c is DropIndexClause { Object: { } o } && IsTransient(o)),
        _ => false,
    };

    /// <summary>True for a data-modification statement whose target is a persistent table.</summary>
    public static bool ModifiesPersistentData(TSqlStatement ast) => ast switch
    {
        InsertStatement insert => !IsTransient(insert.InsertSpecification?.Target),
        UpdateStatement update => !IsTransientTarget(update.UpdateSpecification),
        DeleteStatement delete => !IsTransientTarget(delete.DeleteSpecification),
        MergeStatement merge => !IsTransient(merge.MergeSpecification?.Target),
        SelectStatement { Into: { } into } => !IsTransient(into),
        SelectStatement => false,
        BulkInsertStatement => true,
        DataModificationStatement => true,
        _ => false,
    };
}
