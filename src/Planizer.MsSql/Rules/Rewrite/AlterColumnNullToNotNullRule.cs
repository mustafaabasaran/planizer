using Planizer.Core;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-007: ALTER COLUMN … NOT NULL. Writing NOT NULL in the new definition is the
/// NULL→NOT NULL intent (certain offline): SQL Server scans every row under Sch-M to validate,
/// and the statement fails if any NULL exists.
/// </summary>
public sealed class AlterColumnNullToNotNullRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-RW-007";
    public override string Title => "Altering a column to NOT NULL scans the whole table and fails on NULLs";
    public override Severity DefaultSeverity => Severity.Critical;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (AlterColumnClassifier.Classify(statement.Ast) is not { } facts
                || !facts.Has(AlterColumnChangeKind.NullToNotNull))
            {
                continue;
            }

            var behavior = context.Catalog.Lookup(
                DdlOperationKeys.AlterColumnNullToNotNull, context.Config.TargetVersion, context.Config.Edition);

            if (behavior is null)
            {
                yield return CreateFinding(statement, Severity.Warning,
                    $"No behavior data for altering column {facts.ColumnName} of {facts.TableName} " +
                    "to NOT NULL under the configured target — review manually.",
                    inconclusive: true);
            }
            else
            {
                yield return CreateFinding(statement, Severity.Critical,
                    $"Altering column {facts.ColumnName} of {facts.TableName} to NOT NULL scans the " +
                    "entire table to validate under a Sch-M lock and fails if any NULL exists.",
                    fix: $"UPDATE {facts.TableName} SET {facts.ColumnName} = <default> " +
                         $"WHERE {facts.ColumnName} IS NULL; -- run before this ALTER");
            }
        }
    }
}
