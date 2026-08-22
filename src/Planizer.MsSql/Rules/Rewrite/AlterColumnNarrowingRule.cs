using Planizer.Core;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-006: the new column definition carries an explicit length/precision, so it COULD
/// be a narrowing (var(100)→var(50), precision drop) — data loss, a full rewrite and
/// truncation failures. Offline the current type is unknown, so the rule reports
/// Info + Inconclusive alongside RW-005; with schema (Phase 2) confirmed narrowing is Critical.
/// </summary>
public sealed class AlterColumnNarrowingRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-RW-006";
    public override string Title => "Narrowing a column loses data and risks truncation failures";
    public override Severity DefaultSeverity => Severity.Critical;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (AlterColumnClassifier.Classify(statement.Ast) is not { } facts
                || !facts.Has(AlterColumnChangeKind.UnknownTypeChange)
                || !facts.NewTypeHasExplicitSize)
            {
                continue;
            }

            yield return CreateFinding(statement, Severity.Info,
                $"Cannot compare {facts.NewTypeText} with the current type of {facts.ColumnName} " +
                $"without schema; if this narrows the column, {facts.TableName} is rewritten with " +
                "data loss and truncation failure risk.",
                inconclusive: true);
        }
    }
}
