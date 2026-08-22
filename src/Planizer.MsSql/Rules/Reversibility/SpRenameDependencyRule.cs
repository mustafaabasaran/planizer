using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Reversibility;

/// <summary>
/// MSSQL-REV-003: sp_rename does not update references inside dependent procedures, views or
/// functions (deferred name resolution) — they keep the old name and break at runtime. Column
/// renames are the most dangerous variant (queries, indexes' definitions, computed columns) and
/// report as Critical; every other rename reports as Warning.
/// </summary>
public sealed class SpRenameDependencyRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-REV-003";
    public override string Title => "sp_rename leaves dependent objects pointing at the old name";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (!StatementClassifier.IsProcedureCall(statement.Ast, "sp_rename"))
            {
                continue;
            }

            var isColumnRename = IsColumnRename(statement.Ast);
            var severity = isColumnRename ? Severity.Critical : Severity.Warning;
            var subject = isColumnRename ? "The renamed column" : "The renamed object";

            yield return CreateFinding(statement, severity,
                $"{subject} is not updated inside dependent procedures, views or functions " +
                "(deferred name resolution); they still reference the old name and fail at runtime.",
                fix: "Prefer expand/contract: create the new name, migrate readers, drop the old one " +
                     "in a later release. At minimum check sys.sql_expression_dependencies for " +
                     "references before renaming.");
        }
    }

    /// <summary>True when the @objtype argument (positional 3rd or named) is the literal 'COLUMN'.</summary>
    private static bool IsColumnRename(TSqlStatement ast)
    {
        if (ast is not ExecuteStatement
            {
                ExecuteSpecification.ExecutableEntity: ExecutableProcedureReference procedure,
            })
        {
            return false;
        }

        for (var i = 0; i < procedure.Parameters.Count; i++)
        {
            var parameter = procedure.Parameters[i];
            var isObjType = parameter.Variable?.Name is { } name
                ? name.TrimStart('@').Equals("objtype", StringComparison.OrdinalIgnoreCase)
                : i == 2;

            if (isObjType && parameter.ParameterValue is StringLiteral literal)
            {
                return literal.Value.Equals("COLUMN", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }
}
