using Planizer.Core;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-005: ALTER COLUMN respecifies a type and nothing else explains the statement —
/// no MAX, no NOT NULL/NULL, no COLLATE. Offline the current type is unknown, so widening
/// (metadata-only), fixed-length change (rewrite) and narrowing (data loss) cannot be told
/// apart: the rule does not stay silent, it reports Warning + Inconclusive. With schema
/// (Phase 2) this becomes the conclusive fixed-length-change rewrite rule (Critical).
/// </summary>
public sealed class AlterColumnTypeChangeRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-RW-005";
    public override string Title => "Column type change may rewrite the table depending on the current type";
    public override Severity DefaultSeverity => Severity.Critical;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (AlterColumnClassifier.Classify(statement.Ast) is not { } facts
                || !facts.Has(AlterColumnChangeKind.UnknownTypeChange))
            {
                continue;
            }

            yield return CreateFinding(statement, Severity.Warning,
                $"Column {facts.ColumnName} of {facts.TableName} changes type to {facts.NewTypeText}; " +
                "whether this is a rewrite depends on the current type — verify.",
                fix: "If the types differ in storage (e.g. int→bigint, precision/scale), use " +
                     "expand/contract: add a new column, backfill in batches, then swap.",
                inconclusive: true);
        }
    }
}
