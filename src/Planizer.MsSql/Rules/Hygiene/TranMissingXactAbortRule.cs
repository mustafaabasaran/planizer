using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Hygiene;

/// <summary>
/// MSSQL-TRAN-001: the file opens an explicit transaction but no <c>SET XACT_ABORT ON</c> is in
/// effect at that point. With XACT_ABORT OFF (the default for most client libraries) many
/// run-time errors — a lock timeout, a constraint violation, a conversion error — abort only the
/// failing statement; the batch carries on and the transaction is left open, holding every lock it
/// took, until the connection is closed. One finding per file, anchored to the first BEGIN TRAN.
/// </summary>
public sealed class TranMissingXactAbortRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-TRAN-001";
    public override string Title => "Explicit transaction without SET XACT_ABORT ON";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var file in context.Statements.GroupBy(s => s.Location.File, StringComparer.Ordinal))
        {
            var xactAbortOn = false;

            foreach (var statement in file)
            {
                if (statement.Ast is PredicateSetStatement set && set.Options.HasFlag(SetOptions.XactAbort))
                {
                    xactAbortOn = set.IsOn;
                    continue;
                }

                if (statement.Ast is not BeginTransactionStatement)
                {
                    continue;
                }

                if (!xactAbortOn)
                {
                    yield return CreateFinding(statement, DefaultSeverity,
                        $"This script opens an explicit transaction (line {statement.Location.Line}) " +
                        "with XACT_ABORT OFF; a run-time error such as a lock timeout or constraint " +
                        "violation then aborts only the failing statement, and the transaction stays " +
                        "open — holding its locks — until the connection closes.",
                        "Add SET XACT_ABORT ON; at the top of the script so any error rolls the transaction back and aborts the batch.");
                }

                break; // one finding per file: the first BEGIN TRAN decides
            }
        }
    }
}
