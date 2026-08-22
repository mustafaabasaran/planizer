using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql;

/// <summary>
/// Turns a batch's statement tree into the ordered list of statements that run at deploy time:
/// the batch's top-level statements plus, recursively, the bodies of <c>IF</c>/<c>ELSE</c>,
/// <c>BEGIN…END</c>, <c>WHILE</c> and <c>TRY…CATCH</c>. Pre-order: the wrapper itself is listed
/// (as <see cref="StatementKind.Flow"/>) before its children. Module bodies (procedure, function,
/// trigger, view definitions) are definitions, not migration actions, and are never entered.
/// </summary>
public static class StatementFlattener
{
    /// <summary>Control-flow position of a statement, handed to the factory that builds its <see cref="SqlStatementInfo"/>.</summary>
    public readonly record struct StatementContext(
        int Depth,
        SqlStatementInfo? Parent,
        IfStatement? EnclosingIf,
        bool InElseBranch,
        bool InTryBlock,
        bool InCatchBlock,
        bool InWhileLoop)
    {
        public static StatementContext TopLevel => new(0, null, null, false, false, false, false);
    }

    /// <summary>
    /// Walks <paramref name="topLevel"/> in pre-order, calling <paramref name="create"/> for every
    /// deploy-time statement and appending the result to <paramref name="output"/>.
    /// </summary>
    public static void Flatten(
        IEnumerable<TSqlStatement> topLevel,
        Func<TSqlStatement, StatementContext, SqlStatementInfo> create,
        ICollection<SqlStatementInfo> output)
    {
        foreach (var statement in topLevel)
        {
            Visit(statement, StatementContext.TopLevel, create, output);
        }
    }

    /// <summary>True for the control-flow wrappers whose bodies are flattened.</summary>
    public static bool IsContainer(TSqlStatement statement)
        => statement is IfStatement or BeginEndBlockStatement or WhileStatement or TryCatchStatement;

    private static void Visit(
        TSqlStatement statement,
        StatementContext context,
        Func<TSqlStatement, StatementContext, SqlStatementInfo> create,
        ICollection<SqlStatementInfo> output)
    {
        var info = create(statement, context);
        output.Add(info);

        var inner = context with { Depth = context.Depth + 1, Parent = info };

        switch (statement)
        {
            case IfStatement ifStatement:
                VisitChild(ifStatement.ThenStatement,
                    inner with { EnclosingIf = ifStatement, InElseBranch = false }, create, output);
                VisitChild(ifStatement.ElseStatement,
                    inner with { EnclosingIf = ifStatement, InElseBranch = true }, create, output);
                break;

            case BeginEndBlockStatement block:
                // BEGIN…END is transparent: EnclosingIf / TRY / WHILE flags pass straight through.
                VisitList(block.StatementList, inner, create, output);
                break;

            case WhileStatement whileStatement:
                VisitChild(whileStatement.Statement, inner with { InWhileLoop = true }, create, output);
                break;

            case TryCatchStatement tryCatch:
                // Innermost TRY-CATCH wins: a TRY nested in a CATCH reports InTryBlock, not InCatchBlock.
                VisitList(tryCatch.TryStatements,
                    inner with { InTryBlock = true, InCatchBlock = false }, create, output);
                VisitList(tryCatch.CatchStatements,
                    inner with { InTryBlock = false, InCatchBlock = true }, create, output);
                break;
        }
    }

    private static void VisitChild(
        TSqlStatement? child,
        StatementContext context,
        Func<TSqlStatement, StatementContext, SqlStatementInfo> create,
        ICollection<SqlStatementInfo> output)
    {
        if (child is not null)
        {
            Visit(child, context, create, output);
        }
    }

    private static void VisitList(
        StatementList? list,
        StatementContext context,
        Func<TSqlStatement, StatementContext, SqlStatementInfo> create,
        ICollection<SqlStatementInfo> output)
    {
        if (list is null)
        {
            return;
        }

        foreach (var child in list.Statements)
        {
            Visit(child, context, create, output);
        }
    }
}
