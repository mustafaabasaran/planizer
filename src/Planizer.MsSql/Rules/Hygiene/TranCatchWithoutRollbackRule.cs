using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql.Rules.Hygiene;

/// <summary>
/// MSSQL-TRAN-004: a transaction is opened inside a TRY block, but no enclosing CATCH block rolls
/// it back. When the TRY fails the transaction is left open or doomed (uncommittable); the CATCH
/// then hits error 3998 / 3930 on its first write, and if the batch ends with the transaction open
/// its locks are held until the connection closes. Nested TRY-CATCH is honoured: a CATCH that only
/// rethrows hands the error to the next CATCH outward, so a ROLLBACK in any enclosing CATCH counts.
/// <c>ROLLBACK TRANSACTION savepoint</c> does not: it leaves the transaction open, and fails with
/// error 3931 when the error has doomed it. Anchored to the BEGIN TRAN.
/// </summary>
public sealed class TranCatchWithoutRollbackRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-TRAN-004";
    public override string Title => "BEGIN TRAN inside TRY without ROLLBACK in CATCH";
    public override Severity DefaultSeverity => Severity.Critical;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var paths in TransactionPaths.ByFile(context))
        {
            foreach (var begin in paths.Statements.Where(s => s.Ast is BeginTransactionStatement && s.InTryBlock))
            {
                var tryCatches = TransactionPaths.EnclosingTryCatches(begin).ToList();
                if (tryCatches.Count == 0
                    || tryCatches.Any(tc => paths.CatchBody(tc).Any(s =>
                        s.Ast is RollbackTransactionStatement && !paths.IsRollbackToSavepoint(s))))
                {
                    continue;
                }

                var innermost = tryCatches[0];
                var savepointRollback = tryCatches
                    .SelectMany(paths.CatchBody)
                    .FirstOrDefault(paths.IsRollbackToSavepoint);

                var handling = savepointRollback is null
                    ? "is not rolled back in its CATCH block"
                    : $"is only rolled back to savepoint " +
                      $"{TransactionPaths.TransactionName((TransactionStatement)savepointRollback.Ast)} " +
                      $"in its CATCH block (line {savepointRollback.Location.Line}), which keeps it open — " +
                      "and fails with error 3931 when the error has made it uncommittable";

                yield return CreateFinding(begin, DefaultSeverity,
                    $"The transaction opened at line {begin.Location.Line} inside the TRY block starting " +
                    $"at line {innermost.Location.Line} {handling}; after an " +
                    "error it is left open or uncommittable (error 3998), blocking everything behind its locks.",
                    "Add IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION; as the first statement of the CATCH block.");
            }
        }
    }
}
