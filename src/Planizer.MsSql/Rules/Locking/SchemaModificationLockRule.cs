using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql.Rules.Locking;

/// <summary>
/// MSSQL-LOCK-001: flags every statement that acquires a schema-modification (Sch-M) lock —
/// ALTER TABLE in all its variants (SWITCH included), DROP TABLE, TRUNCATE TABLE and sp_rename.
/// The behavior catalog decides how loud: metadata-only operations hold Sch-M only briefly
/// (Info), everything else holds it for the duration (Warning). No catalog row → the rule does
/// not stay silent; it reports an inconclusive Warning.
/// </summary>
public sealed class SchemaModificationLockRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-LOCK-001";
    public override string Title => "Schema modification lock (Sch-M) blocks all access to the table";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            // Temp tables are session-scoped: their Sch-M lock blocks nobody else.
            if (!IsSchMTrigger(statement.Ast) || DmlTargets.TargetsTransientObject(statement.Ast))
            {
                continue;
            }

            var target = DescribeTarget(statement.Ast);
            var operation = DescribeOperation(statement.Ast, target);
            var behavior = DdlOperationClassifier.GetBehavior(statement, context.Catalog, context.Config);

            if (behavior is null)
            {
                yield return CreateFinding(statement, Severity.Warning,
                    $"{operation} takes a schema-modification (Sch-M) lock on {target}; how long it is held " +
                    "cannot be determined for this operation — review manually.",
                    inconclusive: true);
            }
            else if (behavior.Movement == DataMovement.MetadataOnly)
            {
                yield return CreateFinding(statement, Severity.Info,
                    $"{operation} takes a brief schema-modification (Sch-M) lock on {target}; " +
                    "metadata-only change, blocking is momentary.");
            }
            else
            {
                yield return CreateFinding(statement, Severity.Warning,
                    $"{operation} takes a schema-modification (Sch-M) lock on {target}, held for the duration " +
                    "of the operation; all reads and writes are blocked.");
            }
        }
    }

    private static bool IsSchMTrigger(TSqlStatement ast) => ast switch
    {
        AlterTableStatement => true, // every variant, AlterTableSwitchStatement included
        DropTableStatement => true,
        TruncateTableStatement => true,
        _ => StatementClassifier.IsProcedureCall(ast, "sp_rename"),
    };

    private static string DescribeTarget(TSqlStatement ast) => ast switch
    {
        AlterTableStatement alter => Render(alter.SchemaObjectName),
        DropTableStatement drop when drop.Objects.Count > 0
            => string.Join(", ", drop.Objects.Select(Render)),
        TruncateTableStatement truncate => Render(truncate.TableName),
        _ => TransactionSchMScanner.SpRenameLockTarget(ast) ?? "the renamed object", // sp_rename
    };

    /// <summary>Names the operation (and the column/constraint it touches) so two findings on the same table read differently.</summary>
    private static string DescribeOperation(TSqlStatement ast, string target) => ast switch
    {
        AlterTableAlterColumnStatement alter
            => $"ALTER COLUMN {alter.ColumnIdentifier?.Value ?? "?"}",
        AlterTableAddTableElementStatement add => $"ADD {DescribeAddedElements(add)}",
        AlterTableDropTableElementStatement drop
            => "DROP " + string.Join(", ", drop.AlterTableDropTableElements
                .Select(e => $"{e.TableElementType.ToString().ToUpperInvariant()} {e.Name?.Value ?? "?"}")),
        AlterTableSwitchStatement => "ALTER TABLE ... SWITCH",
        AlterTableTriggerModificationStatement trigger
            => (trigger.TriggerEnforcement == TriggerEnforcement.Enable ? "ENABLE" : "DISABLE") + " TRIGGER",
        AlterTableStatement => "ALTER TABLE",
        DropTableStatement => "DROP TABLE",
        TruncateTableStatement => "TRUNCATE TABLE",
        _ => "sp_rename",
    };

    private static string DescribeAddedElements(AlterTableAddTableElementStatement add)
    {
        var names = new List<string>();
        if (add.Definition is { } definition)
        {
            names.AddRange(definition.ColumnDefinitions.Select(c => $"COLUMN {c.ColumnIdentifier?.Value ?? "?"}"));
            names.AddRange(definition.TableConstraints.Select(c => c switch
            {
                { ConstraintIdentifier.Value: { } n } => $"CONSTRAINT {n}",
                DefaultConstraintDefinition { Column.Value: { } column } => $"DEFAULT FOR {column}",
                _ => "CONSTRAINT",
            }));
        }

        return names.Count == 0 ? "elements" : string.Join(", ", names);
    }

    private static string Render(SchemaObjectName? name)
        => name is null || name.Identifiers.Count == 0
            ? "the table"
            : string.Join(".", name.Identifiers.Select(i => i.Value));
}
