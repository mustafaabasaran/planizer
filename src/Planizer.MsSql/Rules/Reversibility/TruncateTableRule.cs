using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql.Rules.Reversibility;

/// <summary>
/// MSSQL-REV-004: TRUNCATE TABLE nuances. Inside an explicit transaction it CAN be rolled back
/// until COMMIT (a common misconception); outside one there is no rollback window at all. Either
/// way it fails outright when the table is referenced by a foreign key — which a script alone
/// cannot reveal, so the finding is marked inconclusive.
/// </summary>
public sealed class TruncateTableRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-REV-004";
    public override string Title => "TRUNCATE TABLE: rollback window and FK restrictions";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (statement.Ast is not TruncateTableStatement truncate || DmlTargets.IsTransient(truncate.TableName))
            {
                continue;
            }

            var table = Render(truncate.TableName);
            var rollbackNote = context.IsInExplicitTransaction(statement.Index)
                ? "can be rolled back until the enclosing transaction commits"
                : "is not inside an explicit transaction, so there is no rollback window";

            yield return CreateFinding(statement, Severity.Warning,
                $"TRUNCATE TABLE {rollbackNote}; it fails if {table} is referenced by a " +
                "foreign key, which cannot be verified offline.",
                fix: $"If {table} may be FK-referenced or a rollback window is needed, delete in " +
                     $"batches instead: WHILE 1 = 1 BEGIN DELETE TOP (4000) FROM {table} WHERE <predicate>; " +
                     "IF @@ROWCOUNT = 0 BREAK; END",
                inconclusive: true);
        }
    }

    private static string Render(SchemaObjectName? name)
        => name is null || name.Identifiers.Count == 0
            ? "the table"
            : string.Join(".", name.Identifiers.Select(i => i.Value));
}
