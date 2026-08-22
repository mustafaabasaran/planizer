using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql;

/// <summary>One parsed statement plus everything rules need to reason about it.</summary>
public sealed class SqlStatementInfo
{
    public required TSqlStatement Ast { get; init; }
    public required StatementKind Kind { get; init; }
    public required SourceLocation Location { get; init; }

    /// <summary>Raw text of the statement as it appears in the script.</summary>
    public required string Sql { get; init; }

    /// <summary>
    /// Position of the statement in the analyzed script(s); global across files, assigned in
    /// pre-order (a control-flow wrapper precedes the statements nested in it).
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    /// Rule ids suppressed via <c>-- planizer:ignore</c> on or directly above this statement, or
    /// on an enclosing control-flow block (a suppression on an <c>IF</c> covers its body).
    /// </summary>
    public IReadOnlySet<string> SuppressedRuleIds { get; init; } = new HashSet<string>();

    public string? SuppressReason { get; init; }

    /// <summary>
    /// 0-based position of the <c>GO</c>-separated batch this statement belongs to. Like
    /// <see cref="Index"/> the numbering is global across the analyzed files, so a batch index
    /// identifies exactly one batch (see <see cref="MsSqlAnalysisContext.StatementsInBatch"/>).
    /// </summary>
    public required int BatchIndex { get; init; }

    /// <summary>0 = top level of the batch; +1 for every enclosing IF / BEGIN-END / TRY-CATCH / WHILE.</summary>
    public required int Depth { get; init; }

    /// <summary>
    /// The enclosing control-flow statement (<see cref="IfStatement"/>, <see cref="WhileStatement"/>,
    /// <see cref="TryCatchStatement"/>, <see cref="BeginEndBlockStatement"/>); <c>null</c> at the top
    /// level. Follow <c>Parent.Parent…</c> to walk outward.
    /// </summary>
    public SqlStatementInfo? Parent { get; init; }

    /// <summary>Nearest enclosing <c>IF</c>, looking through BEGIN-END / TRY / WHILE wrappers; <c>null</c> if none.</summary>
    public IfStatement? EnclosingIf { get; init; }

    /// <summary>Whether the statement sits in the <c>ELSE</c> branch of <see cref="EnclosingIf"/>.</summary>
    public bool InElseBranch { get; init; }

    /// <summary>Inside the <c>TRY</c> body of the innermost enclosing TRY-CATCH.</summary>
    public bool InTryBlock { get; init; }

    /// <summary>Inside the <c>CATCH</c> body of the innermost enclosing TRY-CATCH.</summary>
    public bool InCatchBlock { get; init; }

    /// <summary>Inside the body of any enclosing <c>WHILE</c> loop.</summary>
    public bool InWhileLoop { get; init; }
}
