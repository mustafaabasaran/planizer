using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql.Parsing;

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
    /// A DELETE/UPDATE whose FROM clause joins other tables is bounded by the join even when
    /// it has no WHERE — it is not an unfiltered full-table write.
    /// </summary>
    public static bool IsJoinFiltered(UpdateDeleteSpecificationBase? spec)
        => spec?.FromClause?.TableReferences is { } refs
           && (refs.Count > 1 || refs.Any(r => r is JoinTableReference));

    /// <summary>Unfiltered write to a persistent table: no WHERE, no TOP, no bounding JOIN, not transient.</summary>
    public static bool IsUnboundedPersistentWrite(UpdateDeleteSpecificationBase? spec)
        => spec is not null
           && spec.WhereClause is null
           && spec.TopRowFilter is null
           && !IsJoinFiltered(spec)
           && !IsTransientTarget(spec);

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

        var alias = name.BaseIdentifier.Value;
        var aliased = spec.FromClause.TableReferences
            .SelectMany(Flatten)
            .OfType<NamedTableReference>()
            .FirstOrDefault(t => t.Alias?.Value is { } a && a.Equals(alias, StringComparison.OrdinalIgnoreCase));

        return aliased?.SchemaObject ?? name;
    }

    /// <summary>Whether the target — written directly or through an alias — is a table variable or temp table.</summary>
    private static bool IsTransientTarget(UpdateDeleteSpecificationBase? spec)
        => IsTransient(spec?.Target) || IsTransient(ResolveTargetTable(spec));

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
