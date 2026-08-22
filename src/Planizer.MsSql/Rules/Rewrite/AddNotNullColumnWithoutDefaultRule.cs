using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-003: ADD COLUMN NOT NULL without a DEFAULT errors out when the table has rows.
/// Offline the row count is unknown, so the message says "fails if {t} has rows" — a Blocker,
/// because a populated production table makes the migration fail outright.
/// </summary>
public sealed class AddNotNullColumnWithoutDefaultRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-RW-003";
    public override string Title => "Adding a NOT NULL column without a default fails on a non-empty table";
    public override Severity DefaultSeverity => Severity.Blocker;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (statement.Ast is not AlterTableAddTableElementStatement add)
            {
                continue;
            }

            var table = SqlNames.Table(add.SchemaObjectName);
            foreach (var column in AddedColumnClassifier.Classify(add))
            {
                if (column.Shape != AddedColumnShape.NotNullNoDefault)
                {
                    continue;
                }

                var behavior = context.Catalog.Lookup(
                    column.OperationKey, context.Config.TargetVersion, context.Config.Edition);

                if (behavior is null)
                {
                    yield return CreateFinding(statement, Severity.Warning,
                        $"No behavior data for adding NOT NULL column {column.ColumnName} (no default) " +
                        $"to {table} under the configured target — review manually.",
                        inconclusive: true);
                }
                else
                {
                    yield return CreateFinding(statement, Severity.Blocker,
                        $"Adding NOT NULL column {column.ColumnName} without a default fails if {table} has rows.",
                        fix: $"Add {column.ColumnName} as nullable, backfill existing rows, then ALTER it " +
                             "to NOT NULL — or declare a DEFAULT constraint on this ADD.");
                }
            }
        }
    }
}
