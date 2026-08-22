using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>Nullability/default shape of one column added by ALTER TABLE … ADD.</summary>
internal enum AddedColumnShape
{
    /// <summary>Nullable, no default: metadata-only (RW-001).</summary>
    NullableNoDefault,

    /// <summary>NOT NULL with a runtime-constant default: edition decides (RW-002).</summary>
    NotNullConstantDefault,

    /// <summary>NOT NULL with a non-constant (per-row or unrecognized) default: rewrite everywhere (RW-002).</summary>
    NotNullNonConstantDefault,

    /// <summary>NOT NULL without any default: fails when the table has rows (RW-003).</summary>
    NotNullNoDefault,
}

/// <summary>One added column the rewrite rules can reason about, with its catalog operation key.</summary>
internal sealed record AddedColumn(
    string ColumnName,
    AddedColumnShape Shape,
    string OperationKey,
    string? DefaultFunctionName,
    bool DefaultIsPerRow = false);

/// <summary>
/// Per-column classification of <see cref="AlterTableAddTableElementStatement"/> for the
/// RW-001..003 rules. Unlike <see cref="DdlOperationClassifier"/> (which names the whole
/// statement by its riskiest element), this yields every plain data column so each one gets
/// its own precise finding. IDENTITY and computed columns are skipped on purpose — they have
/// their own semantics (computed PERSISTED is RW-011's territory). Runtime-constant vs
/// per-row default semantics live in <see cref="DefaultExpressionClassifier"/>.
/// </summary>
internal static class AddedColumnClassifier
{
    public static IEnumerable<AddedColumn> Classify(AlterTableAddTableElementStatement statement)
    {
        foreach (var column in statement.Definition?.ColumnDefinitions ?? [])
        {
            if (column.IdentityOptions is not null || column.ComputedColumnExpression is not null)
            {
                continue;
            }

            var name = column.ColumnIdentifier?.Value ?? "the new column";
            var notNull = column.Constraints.OfType<NullableConstraintDefinition>().Any(c => !c.Nullable);
            var defaultExpression = (column.DefaultConstraint
                ?? column.Constraints.OfType<DefaultConstraintDefinition>().FirstOrDefault())?.Expression;

            if (!notNull)
            {
                if (defaultExpression is null)
                {
                    yield return new AddedColumn(
                        name, AddedColumnShape.NullableNoDefault, DdlOperationKeys.AddColumnNullable, null);
                }

                // Nullable + DEFAULT: existing rows stay NULL, no Task 7 rule speaks about it.
                continue;
            }

            if (defaultExpression is null)
            {
                yield return new AddedColumn(
                    name, AddedColumnShape.NotNullNoDefault, DdlOperationKeys.AddColumnNotNullNoDefault, null);
            }
            else if (DefaultExpressionClassifier.IsRuntimeConstant(defaultExpression))
            {
                yield return new AddedColumn(
                    name, AddedColumnShape.NotNullConstantDefault, DdlOperationKeys.AddColumnNotNullDefaultConst, null);
            }
            else
            {
                yield return new AddedColumn(
                    name,
                    AddedColumnShape.NotNullNonConstantDefault,
                    DdlOperationKeys.AddColumnNotNullDefaultNondet,
                    DefaultExpressionClassifier.DescribeFunction(defaultExpression),
                    DefaultIsPerRow: DefaultExpressionClassifier.IsPerRowFunction(defaultExpression));
            }
        }
    }
}
