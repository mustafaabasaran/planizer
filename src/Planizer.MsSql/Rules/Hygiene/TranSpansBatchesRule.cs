using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql.Rules.Hygiene;

/// <summary>
/// MSSQL-TRAN-003: a transaction that is opened in one <c>GO</c> batch and closed in a later one.
/// A transaction survives <c>GO</c>, but error handling does not: if an intermediate batch fails
/// the closing COMMIT never runs, the transaction stays open, and every following batch silently
/// executes inside it (and is rolled back with it when the connection drops). Up to
/// <see cref="AggregateThreshold"/> such transactions in a file are reported one by one; above
/// that the file gets a single finding (ADR-0001 shape) anchored at the first BEGIN TRAN with the
/// count and the first examples — the EF Core idempotent script shape (<c>BEGIN TRANSACTION; GO
/// … COMMIT; GO</c> per migration) would otherwise produce hundreds of identical warnings per file.
/// Transactions whose BEGIN TRAN is suppressed for this rule leave the count.
/// </summary>
public sealed class TranSpansBatchesRule : MsSqlRuleBase
{
    /// <summary>More than this many spanning transactions in one file collapse into a single finding.</summary>
    public const int AggregateThreshold = 5;

    private const int ExampleCount = 3;

    public override string Id => "MSSQL-TRAN-003";
    public override string Title => "Transaction spans GO batches";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var paths in TransactionPaths.ByFile(context))
        {
            var spanning = paths.Closed
                .Where(c => c.End.BatchIndex > c.Begin.BatchIndex)
                .OrderBy(c => c.Begin.Index)
                .ToList();

            var counted = spanning.Where(c => !c.Begin.SuppressedRuleIds.Contains(Id)).ToList();

            if (counted.Count <= AggregateThreshold)
            {
                // Suppressed ones are reported too; the analyzer marks them as suppressed.
                foreach (var (begin, end) in spanning)
                {
                    yield return ReportOne(begin, end);
                }

                continue;
            }

            yield return ReportFile(counted);
        }
    }

    private Finding ReportOne(SqlStatementInfo begin, SqlStatementInfo end)
    {
        var span = end.BatchIndex - begin.BatchIndex;
        return CreateFinding(begin, DefaultSeverity,
            $"The transaction opened at line {begin.Location.Line} is {Verb(end)} at line " +
            $"{end.Location.Line}, {Batches(span)} later; if a batch in " +
            "between fails, the transaction stays open and the remaining batches run inside it.",
            "Keep BEGIN TRAN and its COMMIT in the same batch: remove the GO separators between them, or commit before each GO and open a new transaction after it.");
    }

    private Finding ReportFile(IReadOnlyList<TransactionPaths.ClosedTransaction> spanning)
    {
        var (begin, end) = spanning[0];
        var also = spanning
            .Skip(1)
            .Take(ExampleCount)
            .Select(c => c.Begin.Location.Line.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return CreateFinding(begin, DefaultSeverity,
            $"{spanning.Count} transactions in this file are opened in one GO batch and closed in a later one " +
            $"(first: line {begin.Location.Line}, {Verb(end)} at line {end.Location.Line}, " +
            $"{Batches(end.BatchIndex - begin.BatchIndex)} later; also lines {string.Join(", ", also)}" +
            (spanning.Count > ExampleCount + 1 ? ", …" : "") +
            "); if a batch in between fails, that transaction stays open and the remaining batches run inside it.",
            "Keep each BEGIN TRAN and its COMMIT in the same batch: remove the GO separators between them, or commit before each GO and open a new transaction after it. " +
            "EF Core idempotent scripts get this shape from their per-migration BEGIN TRANSACTION; GO … COMMIT; GO: generate them with --no-transactions and let the migration runner own the transaction.");
    }

    private static string Verb(SqlStatementInfo end)
        => end.Ast is RollbackTransactionStatement ? "rolled back" : "committed";

    private static string Batches(int span) => $"{span} GO batch{(span == 1 ? "" : "es")}";
}
