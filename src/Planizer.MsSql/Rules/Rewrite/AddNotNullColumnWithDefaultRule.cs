using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-002: ADD COLUMN NOT NULL with a DEFAULT — the behavior is edition dependent, read
/// from the catalog: a runtime-constant default on Enterprise/Azure is metadata-only (Info);
/// on Standard/Express the entire table is rewritten (Critical); a per-row default (NEWID(),
/// NEWSEQUENTIALID()) forces the rewrite on every edition (Critical). Statement-level functions
/// like GETDATE() are runtime constants despite being non-deterministic — they keep the fast
/// path (see <see cref="DefaultExpressionClassifier"/>).
/// </summary>
public sealed class AddNotNullColumnWithDefaultRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-RW-002";
    public override string Title => "Adding a NOT NULL column with a default may rewrite the entire table";
    public override Severity DefaultSeverity => Severity.Critical;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        var edition = TargetParser.EditionToken(context.Config.Edition);

        foreach (var statement in context.Statements)
        {
            if (statement.Ast is not AlterTableAddTableElementStatement add)
            {
                continue;
            }

            var table = SqlNames.Table(add.SchemaObjectName);
            foreach (var column in AddedColumnClassifier.Classify(add))
            {
                if (column.Shape is not (AddedColumnShape.NotNullConstantDefault
                    or AddedColumnShape.NotNullNonConstantDefault))
                {
                    continue;
                }

                var behavior = context.Catalog.Lookup(
                    column.OperationKey, context.Config.TargetVersion, context.Config.Edition);

                if (behavior is null)
                {
                    yield return CreateFinding(statement, Severity.Warning,
                        $"No behavior data for adding NOT NULL column {column.ColumnName} with a default " +
                        $"to {table} under the configured target — review manually.",
                        inconclusive: true);
                }
                else if (column.Shape == AddedColumnShape.NotNullNonConstantDefault)
                {
                    var defaultText = (column.DefaultIsPerRow, column.DefaultFunctionName) switch
                    {
                        (true, { } name) => $"per-row default {name} (evaluated for every row)",
                        (false, { } name) => $"default {name}, which is not a recognized runtime constant,",
                        _ => "a non-constant default",
                    };
                    yield return CreateFinding(statement, Severity.Critical,
                        $"Adding NOT NULL column {column.ColumnName} to {table} with {defaultText} " +
                        "rewrites the entire table on every edition (breaks the metadata-only fast path).",
                        fix: BackfillFix(table, column.ColumnName));
                }
                else if (behavior.Movement == DataMovement.MetadataOnly)
                {
                    yield return CreateFinding(statement, Severity.Info,
                        $"Adding NOT NULL column {column.ColumnName} with a runtime-constant default " +
                        $"to {table} is metadata-only on {edition} edition.");
                }
                else
                {
                    yield return CreateFinding(statement, Severity.Critical,
                        $"Adding NOT NULL column {column.ColumnName} with a default to {table} " +
                        $"rewrites the entire table on {edition} edition.",
                        fix: BackfillFix(table, column.ColumnName));
                }
            }
        }
    }

    private static string BackfillFix(string table, string column)
        => "Run during low traffic, or use expand/contract: add the column as nullable, "
           + $"backfill in batches, then ALTER TABLE {table} ALTER COLUMN {column} ... NOT NULL;";
}
