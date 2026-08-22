using Planizer.Core;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-004: ALTER COLUMN to a MAX type (varchar(MAX), nvarchar(MAX), varbinary(MAX)) is a
/// size-of-data rewrite regardless of the old type — certain offline, unlike ordinary widening
/// (metadata-only, but needs schema to confirm; that branch arrives with Phase 2 providers).
/// </summary>
public sealed class AlterColumnToMaxRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-RW-004";
    public override string Title => "Altering a column to a MAX type rewrites the column data";
    public override Severity DefaultSeverity => Severity.Critical;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (AlterColumnClassifier.Classify(statement.Ast) is not { } facts
                || !facts.Has(AlterColumnChangeKind.WidenToMax))
            {
                continue;
            }

            var behavior = context.Catalog.Lookup(
                DdlOperationKeys.AlterColumnWidenToMax, context.Config.TargetVersion, context.Config.Edition);

            if (behavior is null)
            {
                yield return CreateFinding(statement, Severity.Warning,
                    $"No behavior data for altering column {facts.ColumnName} of {facts.TableName} " +
                    $"to {facts.NewTypeText} under the configured target — review manually.",
                    inconclusive: true);
            }
            else
            {
                yield return CreateFinding(statement, Severity.Critical,
                    $"Altering column {facts.ColumnName} of {facts.TableName} to {facts.NewTypeText} " +
                    "is a size-of-data operation: the column data is rewritten under a Sch-M lock.");
            }
        }
    }
}
