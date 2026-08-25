using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql.Rules.Reversibility;

/// <summary>
/// MSSQL-REV-002: a state-changing statement for which <see cref="RollbackScriptBuilder"/> could
/// not generate an inverse — a manual rollback script is needed. DDL is reported per statement
/// (Warning); DML is summarised once per file (Info), see ADR-0001. Stays quiet where the warning
/// would be noise on top of a stronger signal: irreversible statements (REV-001 — no script can
/// restore that data) and dynamic SQL (DYN-001 — contents unknown).
/// </summary>
public sealed class MissingRollbackRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-REV-002";
    public override string Title => "No automatic rollback statement could be generated";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        if (!context.Config.Rollback)
        {
            yield break; // rollback analysis is opt-in (--rollback); see ADR-0003
        }

        var dmlByFile = new Dictionary<string, List<SqlStatementInfo>>(StringComparer.Ordinal);

        foreach (var statement in context.Statements)
        {
            if (!RollbackScriptBuilder.RequiresRollback(statement)
                || statement.Kind == StatementKind.Dynamic
                || RollbackScriptBuilder.TryReverse(statement) is not null
                || IsIrreversible(statement, context))
            {
                continue;
            }

            if (statement.Kind == StatementKind.Dml)
            {
                // INSERT/UPDATE/DELETE almost never have a derivable inverse, and seed/cleanup
                // scripts are full of them: one summary per file, not a warning per statement
                // (ADR-0001). Statements suppressed for this rule leave the count.
                if (statement.SuppressedRuleIds.Contains(Id))
                {
                    continue;
                }

                if (!dmlByFile.TryGetValue(statement.Location.File, out var list))
                {
                    dmlByFile[statement.Location.File] = list = [];
                }

                list.Add(statement);
                continue;
            }

            yield return CreateFinding(statement, Severity.Warning,
                $"No automatic inverse exists for `{DescribeStatement(statement)}`; " +
                "a manual rollback script is needed for it.");
        }

        foreach (var statements in dmlByFile.Values)
        {
            var count = statements.Count;
            var verbs = statements
                .GroupBy(s => Verb(s.Ast))
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key}\u00d7{g.Count()}");

            yield return CreateFinding(statements[0], Severity.Info,
                $"{count} data-modification statement{(count == 1 ? "" : "s")} in this file " +
                $"{(count == 1 ? "has" : "have")} no automatic inverse ({string.Join(", ", verbs)}); " +
                "the rollback script is incomplete \u2014 write the rollback by hand.");
        }
    }

    private static string Verb(Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement ast) => ast switch
    {
        Microsoft.SqlServer.TransactSql.ScriptDom.InsertStatement => "INSERT",
        Microsoft.SqlServer.TransactSql.ScriptDom.UpdateStatement => "UPDATE",
        Microsoft.SqlServer.TransactSql.ScriptDom.DeleteStatement => "DELETE",
        Microsoft.SqlServer.TransactSql.ScriptDom.MergeStatement => "MERGE",
        Microsoft.SqlServer.TransactSql.ScriptDom.SelectStatement => "SELECT INTO",
        Microsoft.SqlServer.TransactSql.ScriptDom.BulkInsertStatement => "BULK INSERT",
        _ => "DML",
    };

    /// <summary>
    /// Mirrors REV-001's trigger set so the two rules never double-flag a statement — including
    /// its join analysis: a DELETE whose FROM clause holds a join that cannot drop target rows is
    /// REV-001's Critical, while one whose join may or may not bound it (INNER JOIN, CROSS APPLY)
    /// is left to this rule's per-file DML summary.
    /// </summary>
    private static bool IsIrreversible(SqlStatementInfo statement, MsSqlAnalysisContext context)
    {
        if (statement.Ast is Microsoft.SqlServer.TransactSql.ScriptDom.DeleteStatement delete)
        {
            return DmlTargets.IsUnboundedPersistentWrite(delete.DeleteSpecification);
        }

        return statement.Kind == StatementKind.Ddl
            && DdlOperationClassifier.GetBehavior(statement, context.Catalog, context.Config) is
                { Reversible: Planizer.MsSql.Reversibility.No };
    }
}
