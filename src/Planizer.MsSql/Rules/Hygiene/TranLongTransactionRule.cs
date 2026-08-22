using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Hygiene;

/// <summary>
/// MSSQL-TRAN-006: one explicit transaction wraps many statements. Every lock any of them takes is
/// held until the final COMMIT, so the blocking window is the whole transaction's run time, not
/// one statement's. Counts the statements that do work (DDL / DML / procedure calls); control-flow
/// wrappers, SET, DECLARE and PRINT are not counted. LOCK-007 looks at the number of Sch-M locks
/// in a transaction; this rule looks at its length. Anchored to the BEGIN TRAN.
/// </summary>
public sealed class TranLongTransactionRule : MsSqlRuleBase
{
    /// <summary>Statements (excluding control flow) from which a transaction counts as long.</summary>
    public const int Threshold = 25;

    public override string Id => "MSSQL-TRAN-006";
    public override string Title => "Long explicit transaction";
    public override Severity DefaultSeverity => Severity.Info;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var scope in context.Transactions)
        {
            var working = scope.StatementIndices
                .Select(context.StatementAt)
                .Count(DoesWork);

            if (working < Threshold)
            {
                continue;
            }

            var begin = context.StatementAt(scope.BeginIndex);
            yield return CreateFinding(begin, DefaultSeverity,
                $"This transaction wraps {working} statements; every lock any of them takes is held " +
                "until the final COMMIT, so the blocking window is the run time of the whole transaction.",
                "Split independent steps into separate transactions (for example one per table or per batch of rows) so locks are released as each step commits.");
        }
    }

    /// <summary>
    /// DDL, DML, DCL, dynamic SQL and procedure calls. Control flow is excluded by kind; the SET
    /// family is excluded by AST type as well, because the classifier files <c>SET NOCOUNT ON</c>
    /// and friends (<see cref="PredicateSetStatement"/>) under <see cref="StatementKind.Other"/>.
    /// </summary>
    private static bool DoesWork(SqlStatementInfo statement)
        => statement.Kind != StatementKind.Flow
            && statement.Ast is not (SetOnOffStatement or SetCommandStatement or SetTransactionIsolationLevelStatement);
}
