using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-001: ADD COLUMN nullable without a default is metadata-only — an explanatory
/// "safe" finding so the reviewer knows this statement was analyzed and is harmless.
/// Behavior comes from the catalog (<c>add_column_nullable</c>); a missing row does not
/// silence the rule, it reports inconclusively.
/// </summary>
public sealed class AddNullableColumnRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-RW-001";
    public override string Title => "Adding a nullable column without a default is metadata-only";
    public override Severity DefaultSeverity => Severity.Info;

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
                if (column.Shape != AddedColumnShape.NullableNoDefault)
                {
                    continue;
                }

                var behavior = context.Catalog.Lookup(
                    column.OperationKey, context.Config.TargetVersion, context.Config.Edition);

                if (behavior is null)
                {
                    yield return CreateFinding(statement, Severity.Warning,
                        $"No behavior data for adding nullable column {column.ColumnName} to {table} " +
                        "under the configured target — review manually.",
                        inconclusive: true);
                }
                else
                {
                    yield return CreateFinding(statement, Severity.Info,
                        $"Adding nullable column {column.ColumnName} (no default) to {table} is " +
                        "metadata-only; existing rows are not touched — safe.");
                }
            }
        }
    }
}
