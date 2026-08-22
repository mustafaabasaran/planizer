using Planizer.Core;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-008: ALTER COLUMN … NULL. Writing NULL explicitly is the NOT NULL→NULL intent
/// (certain offline): a metadata-only change — an explanatory "safe" finding.
/// </summary>
public sealed class AlterColumnNotNullToNullRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-RW-008";
    public override string Title => "Altering a column to NULL is metadata-only";
    public override Severity DefaultSeverity => Severity.Info;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (AlterColumnClassifier.Classify(statement.Ast) is not { } facts
                || !facts.Has(AlterColumnChangeKind.NotNullToNull))
            {
                continue;
            }

            var behavior = context.Catalog.Lookup(
                DdlOperationKeys.AlterColumnNotNullToNull, context.Config.TargetVersion, context.Config.Edition);

            if (behavior is null)
            {
                yield return CreateFinding(statement, Severity.Warning,
                    $"No behavior data for altering column {facts.ColumnName} of {facts.TableName} " +
                    "to NULL under the configured target — review manually.",
                    inconclusive: true);
            }
            else
            {
                yield return CreateFinding(statement, Severity.Info,
                    $"Altering column {facts.ColumnName} of {facts.TableName} to NULL is " +
                    "metadata-only; no data is touched — safe.");
            }
        }
    }
}
