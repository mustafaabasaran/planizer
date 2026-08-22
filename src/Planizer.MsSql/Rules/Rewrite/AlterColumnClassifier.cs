using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// Classification of an ALTER TABLE … ALTER COLUMN statement. Mirrors the operation keys of the
/// behavior catalog; offline (no schema) only the intent-certain kinds are ever produced —
/// the old column type is unknown, so widen-vs-narrow and fixed-length changes cannot be told
/// apart and collapse into <see cref="UnknownTypeChange"/>.
/// </summary>
public enum AlterColumnChangeKind
{
    /// <summary>varchar/nvarchar/varbinary n→m with m&gt;n (metadata-only). Needs schema; never produced offline.</summary>
    WidenVarLen,

    /// <summary>New type is a MAX type — size-of-data rewrite regardless of the old type (certain offline).</summary>
    WidenToMax,

    /// <summary>Fixed-length type change (int→bigint, precision/scale). Needs schema; never produced offline.</summary>
    FixedLenChange,

    /// <summary>Narrowing (var(100)→var(50), precision drop). Needs schema; never produced offline.</summary>
    Narrow,

    /// <summary>The new definition says NOT NULL — the intent is NULL→NOT NULL (certain offline).</summary>
    NullToNotNull,

    /// <summary>The new definition says NULL explicitly — the intent is NOT NULL→NULL (certain offline).</summary>
    NotNullToNull,

    /// <summary>The new definition carries a COLLATE clause (certain offline).</summary>
    Collation,

    /// <summary>A type is respecified but no certain signal explains it; verify against the current type.</summary>
    UnknownTypeChange,
}

/// <summary>
/// What one ALTER COLUMN statement reveals on its own. <see cref="Changes"/> can hold several
/// kinds at once (e.g. <c>nvarchar(MAX) NOT NULL</c> → WidenToMax + NullToNotNull);
/// <see cref="AlterColumnChangeKind.UnknownTypeChange"/> appears only when no certain kind does.
/// </summary>
public sealed record AlterColumnFacts(
    string TableName,
    string ColumnName,
    string NewTypeText,
    bool NewTypeIsMax,
    bool NewTypeHasExplicitSize,
    IReadOnlyList<AlterColumnChangeKind> Changes)
{
    public bool Has(AlterColumnChangeKind kind) => Changes.Contains(kind);
}

/// <summary>
/// Shared helper of the RW-004..009 rules: extracts everything the RW rules need from an
/// <see cref="AlterTableAlterColumnStatement"/>. Offline decision (from the plan, binding):
/// MAX → RW-004 certain; NOT NULL → RW-007 certain; NULL → RW-008 certain; COLLATE → RW-009
/// certain; any other type respecification → RW-005 inconclusive (plus RW-006 inconclusive
/// when an explicit length/precision means it could narrow).
/// </summary>
public static class AlterColumnClassifier
{
    /// <summary>Facts for the statement; <c>null</c> when it is not an ALTER COLUMN.</summary>
    public static AlterColumnFacts? Classify(TSqlStatement statement)
    {
        if (statement is not AlterTableAlterColumnStatement alter)
        {
            return null;
        }

        var changes = new List<AlterColumnChangeKind>();

        if (alter.Collation is not null)
        {
            changes.Add(AlterColumnChangeKind.Collation);
        }

        switch (alter.AlterTableAlterColumnOption)
        {
            case AlterTableAlterColumnOption.NotNull:
                changes.Add(AlterColumnChangeKind.NullToNotNull);
                break;
            case AlterTableAlterColumnOption.Null:
                changes.Add(AlterColumnChangeKind.NotNullToNull);
                break;
        }

        var isMax = false;
        var hasExplicitSize = false;
        if (alter.DataType is ParameterizedDataTypeReference parameterized)
        {
            isMax = parameterized.Parameters.Any(p => p.LiteralType == LiteralType.Max);
            hasExplicitSize = parameterized.Parameters.Any(p => p.LiteralType == LiteralType.Integer);
        }

        if (isMax)
        {
            changes.Add(AlterColumnChangeKind.WidenToMax);
        }

        if (changes.Count == 0 && alter.DataType is not null)
        {
            changes.Add(AlterColumnChangeKind.UnknownTypeChange);
        }

        return new AlterColumnFacts(
            TableName: SqlNames.Table(alter.SchemaObjectName),
            ColumnName: alter.ColumnIdentifier?.Value ?? "the column",
            NewTypeText: SqlNames.DataType(alter.DataType),
            NewTypeIsMax: isMax,
            NewTypeHasExplicitSize: hasExplicitSize,
            Changes: changes);
    }
}
