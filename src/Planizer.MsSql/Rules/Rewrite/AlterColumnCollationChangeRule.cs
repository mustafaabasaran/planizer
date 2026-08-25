using Planizer.Core;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-009: ALTER COLUMN with a COLLATE clause. CI measurement against a real server showed
/// the swap itself is metadata-only while the code page stays the same; a varchar column moving
/// to a different code page is converted (size-of-data), and every index, constraint or statistic
/// depending on the column must be dropped first or the statement fails. The current collation is
/// unknown offline, so the finding is inconclusive rather than a hard rewrite claim.
/// </summary>
public sealed class AlterColumnCollationChangeRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-RW-009";
    public override string Title => "Changing a column collation may convert the data and needs dependent objects dropped";
    public override Severity DefaultSeverity => Severity.Warning;

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
                yield return CreateFinding(statement, Severity.Warning,
                    $"Changing the collation of column {facts.ColumnName} on {facts.TableName} " +
                    "is metadata-only while the code page stays the same, but a varchar column " +
                    "moving to a different code page is converted (size-of-data), and indexes, " +
                    "constraints or statistics depending on the column must be dropped first or " +
                    "the statement fails; the current collation is unknown offline.",
                    inconclusive: true);
            }
        }
    }
}
