using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql.Rules.Hygiene;

/// <summary>
/// MSSQL-TRAN-005: a CATCH block that does not rethrow. The batch ends successfully, the migration
/// runner records the script as applied, and the schema is left half-changed with nobody told.
/// A rethrow is <c>THROW</c>, <c>RAISERROR</c> with severity 11 or higher (a non-literal severity
/// is assumed to come from <c>ERROR_SEVERITY()</c>), or a call to a procedure whose name contains
/// "throw" / "raise". Anchored to the <c>BEGIN TRY</c>.
/// </summary>
public sealed class TranCatchSwallowsErrorRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-TRAN-005";
    public override string Title => "CATCH block swallows the error";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var paths in TransactionPaths.ByFile(context))
        {
            foreach (var tryCatch in paths.Statements.Where(s => s.Ast is TryCatchStatement))
            {
                var catchBody = paths.CatchBody(tryCatch);
                if (TransactionPaths.Rethrows(catchBody))
                {
                    continue;
                }

                var shape = catchBody.Count == 0
                    ? "is empty"
                    : catchBody.Any(s => s.Ast is RaiseErrorStatement)
                        ? "only raises an informational message (RAISERROR severity below 11 does not fail the batch)"
                        : "neither rethrows nor fails the batch";

                yield return CreateFinding(tryCatch, DefaultSeverity,
                    $"The CATCH block of the TRY starting at line {tryCatch.Location.Line} {shape}; an " +
                    "error inside the TRY is swallowed, the script reports success, and the migration " +
                    "runner marks a half-applied script as done.",
                    "End the CATCH block with THROW; (or RAISERROR(..., 16, 1)) so the failure reaches the caller.");
            }
        }
    }
}
