using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql.Rules.Failure;

/// <summary>
/// Shared AST-walking helpers for the failure-risk family: which part of a statement belongs to
/// the statement itself (and not to statements nested in it), which statements are module
/// definitions, and how to render a fragment back to text.
/// </summary>
internal static class StatementScan
{
    /// <summary>
    /// Procedure / function / trigger / view definitions. Their bodies are not deploy-time
    /// statements (the flattener never enters them) and are left alone by rules that walk the AST.
    /// </summary>
    public static bool IsModuleDefinition(TSqlStatement ast)
        => ast is ProcedureStatementBodyBase or TriggerStatementBody or ViewStatementBody;

    /// <summary>
    /// The fragments a rule should walk for this statement without visiting its nested
    /// statements twice: the predicate of an <c>IF</c> / <c>WHILE</c> (their bodies are separate
    /// <see cref="SqlStatementInfo"/> entries), nothing for <c>BEGIN…END</c> / <c>TRY…CATCH</c>,
    /// and the whole AST otherwise.
    /// </summary>
    public static IEnumerable<TSqlFragment> OwnFragments(SqlStatementInfo statement) => statement.Ast switch
    {
        IfStatement { Predicate: { } predicate } => [predicate],
        WhileStatement { Predicate: { } predicate } => [predicate],
        IfStatement or WhileStatement or BeginEndBlockStatement or TryCatchStatement => [],
        _ => [statement.Ast],
    };

    /// <summary>Source text of a fragment, as written (token stream slice).</summary>
    public static string Text(TSqlFragment fragment)
    {
        if (fragment.ScriptTokenStream is null || fragment.FirstTokenIndex < 0 || fragment.LastTokenIndex < 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = fragment.FirstTokenIndex; i <= fragment.LastTokenIndex; i++)
        {
            builder.Append(fragment.ScriptTokenStream[i].Text);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads the string-literal arguments of an <c>EXEC sp_rename</c> call (positional or named
    /// <c>@objname</c> / <c>@newname</c> / <c>@objtype</c>); <c>null</c> when the statement is not a
    /// static sp_rename call or the names are not literals.
    /// </summary>
    public static (string ObjName, string NewName, string? ObjType)? SpRenameArguments(TSqlStatement ast)
    {
        if (!StatementClassifier.IsProcedureCall(ast, "sp_rename")
            || ast is not ExecuteStatement { ExecuteSpecification.ExecutableEntity: ExecutableProcedureReference procedure })
        {
            return null;
        }

        string? objName = null, newName = null, objType = null;
        for (var i = 0; i < procedure.Parameters.Count; i++)
        {
            var parameter = procedure.Parameters[i];
            if (parameter.ParameterValue is not StringLiteral literal)
            {
                continue;
            }

            var slot = parameter.Variable?.Name?.TrimStart('@').ToLowerInvariant()
                ?? i switch { 0 => "objname", 1 => "newname", 2 => "objtype", _ => "?" };

            switch (slot)
            {
                case "objname": objName ??= literal.Value; break;
                case "newname": newName ??= literal.Value; break;
                case "objtype": objType ??= literal.Value; break;
            }
        }

        return objName is null || newName is null ? null : (objName, newName, objType);
    }
}
