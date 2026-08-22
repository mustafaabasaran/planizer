using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql.Rules.Hygiene;

/// <summary>
/// MSSQL-TRAN-002: unbalanced transaction control on the main path. Either a <c>BEGIN TRAN</c> is
/// never committed or rolled back before the end of the file (a ROLLBACK that lives only in a
/// CATCH block is the error path, not a way out on success), or a <c>COMMIT</c> / <c>ROLLBACK</c>
/// runs with no transaction open (errors 3902 / 3903). IF/ELSE branches, RETURN-ing branches,
/// nested transactions, savepoints and <c>IF @@TRANCOUNT &gt; 0</c> guards are all understood
/// (see <see cref="TransactionPaths"/>).
/// </summary>
public sealed class TranUnbalancedTransactionRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-TRAN-002";
    public override string Title => "Unbalanced BEGIN TRAN / COMMIT / ROLLBACK";
    public override Severity DefaultSeverity => Severity.Critical;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var paths in TransactionPaths.ByFile(context))
        {
            foreach (var begin in paths.LeftOpen)
            {
                yield return CreateFinding(begin, DefaultSeverity,
                    $"The transaction opened at line {begin.Location.Line} is never committed or rolled " +
                    "back on the success path; it is still open when the script ends, so its locks are " +
                    "held and every later batch runs inside it until the connection closes.",
                    "Add COMMIT; (or ROLLBACK;) on the main path — a ROLLBACK inside CATCH only covers the error path.");
            }

            foreach (var stray in paths.Unmatched)
            {
                var (verb, error) = stray.Ast is RollbackTransactionStatement
                    ? ("ROLLBACK", 3903)
                    : ("COMMIT", 3902);

                yield return CreateFinding(stray, DefaultSeverity,
                    $"{verb} at line {stray.Location.Line} has no open transaction to close on this " +
                    $"path; SQL Server raises error {error} (\"The {verb} TRANSACTION request has no " +
                    "corresponding BEGIN TRANSACTION\").",
                    $"Pair it with a BEGIN TRAN, or guard it: IF @@TRANCOUNT > 0 {verb};");
            }
        }
    }
}
