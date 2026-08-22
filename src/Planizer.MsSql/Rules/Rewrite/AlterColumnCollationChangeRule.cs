using Planizer.Core;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-009: ALTER COLUMN with a COLLATE clause rewrites the column data, and every index,
/// constraint or statistic depending on the column must be dropped first or the statement fails.
/// </summary>
public sealed class AlterColumnCollationChangeRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-RW-009";
    public override string Title => "Changing a column collation rewrites the column and needs dependent indexes dropped";
    public override Severity DefaultSeverity => Severity.Critical;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (AlterColumnClassifier.Classify(statement.Ast) is not { } facts
                || !facts.Has(AlterColumnChangeKind.Collation))
            {
                continue;
            }

            var behavior = context.Catalog.Lookup(
                DdlOperationKeys.AlterColumnCollation, context.Config.TargetVersion, context.Config.Edition);

            if (behavior is null)
            {
                yield return CreateFinding(statement, Severity.Warning,
                    $"No behavior data for changing the collation of column {facts.ColumnName} " +
                    $"of {facts.TableName} under the configured target — review manually.",
                    inconclusive: true);
            }
            else
            {
                yield return CreateFinding(statement, Severity.Critical,
                    $"Changing the collation of column {facts.ColumnName} on {facts.TableName} " +
                    "rewrites the column data; indexes, constraints and statistics that depend " +
                    "on the column must be dropped first or the statement fails.");
            }
        }
    }
}
