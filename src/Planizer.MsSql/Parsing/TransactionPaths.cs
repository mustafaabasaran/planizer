using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql.Parsing;

/// <summary>
/// Control-flow-aware transaction accounting for one file, shared by the MSSQL-TRAN rules.
/// Walks the statement tree (rebuilt from <see cref="SqlStatementInfo.Parent"/>) along the
/// <b>main path</b> — the path a script takes when nothing fails: TRY bodies are entered, CATCH
/// bodies are not; the two branches of an IF are simulated separately and the script continues
/// with the branch that changed the transaction state — most-open wins when both did; a branch
/// ending in RETURN / THROW / GOTO leaves the script and is never continued; a RETURN / THROW
/// in the list being walked ends it (what follows — typically a <c>label:</c> error handler
/// reached only by GOTO — is the error path); a GOTO to a later label in the same list jumps
/// to it. <c>ROLLBACK</c> closes every open transaction, <c>COMMIT</c> closes one,
/// <c>ROLLBACK</c> to a savepoint closes nothing.
/// </summary>
public sealed class TransactionPaths
{
    private readonly ILookup<SqlStatementInfo, SqlStatementInfo> _children;

    private TransactionPaths(
        IReadOnlyList<SqlStatementInfo> statements,
        ILookup<SqlStatementInfo, SqlStatementInfo> children,
        IReadOnlyList<ClosedTransaction> closed,
        IReadOnlyList<SqlStatementInfo> leftOpen,
        IReadOnlyList<SqlStatementInfo> unmatched,
        IReadOnlySet<string> savepointNames)
    {
        Statements = statements;
        _children = children;
        Closed = closed;
        LeftOpen = leftOpen;
        Unmatched = unmatched;
        SavepointNames = savepointNames;
    }

    /// <summary>A <c>BEGIN TRAN</c> and the main-path <c>COMMIT</c> / <c>ROLLBACK</c> that closes it.</summary>
    public sealed record ClosedTransaction(SqlStatementInfo Begin, SqlStatementInfo End);

    /// <summary>The file's statements in script order (as handed in).</summary>
    public IReadOnlyList<SqlStatementInfo> Statements { get; }

    /// <summary>Every BEGIN TRAN that is closed on the main path, with the statement that closes it.</summary>
    public IReadOnlyList<ClosedTransaction> Closed { get; }

    /// <summary>BEGIN TRANs still open when the main path reaches the end of the file.</summary>
    public IReadOnlyList<SqlStatementInfo> LeftOpen { get; }

    /// <summary>
    /// COMMIT / ROLLBACK statements reached on the main path with no transaction open and no
    /// <c>@@TRANCOUNT</c> / <c>XACT_STATE()</c> guard around them (errors 3902 / 3903 at run time).
    /// </summary>
    public IReadOnlyList<SqlStatementInfo> Unmatched { get; }

    /// <summary>Every <c>SAVE TRANSACTION</c> name in the file (lower-cased), whatever path it is on.</summary>
    public IReadOnlySet<string> SavepointNames { get; }

    /// <summary>Builds the accounting for one file's statements (pre-order, as produced by the parser).</summary>
    public static TransactionPaths Build(IEnumerable<SqlStatementInfo> fileStatements)
    {
        var statements = fileStatements.ToList();
        var children = statements
            .Where(s => s.Parent is not null)
            .ToLookup(s => s.Parent!);

        var savepoints = statements
            .Select(s => s.Ast as SaveTransactionStatement)
            .Select(save => save is null ? null : NameOf(save))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var collector = new Collector();
        var state = new State();
        WalkList(statements.Where(s => s.Parent is null), children, state, collector);

        return new TransactionPaths(statements, children, collector.Closed, state.Open.ToList(), collector.Unmatched, savepoints);
    }

    /// <summary>
    /// Whether the statement is <c>ROLLBACK TRANSACTION name</c> where <c>name</c> is a savepoint
    /// of this file: it undoes work back to the savepoint and leaves the transaction open —
    /// and fails outright (error 3931) when the transaction is doomed.
    /// </summary>
    public bool IsRollbackToSavepoint(SqlStatementInfo statement)
        => statement.Ast is RollbackTransactionStatement rollback
           && NameOf(rollback) is { } name
           && SavepointNames.Contains(name);

    /// <summary>One <see cref="TransactionPaths"/> per analyzed file, in first-appearance order.</summary>
    public static IEnumerable<TransactionPaths> ByFile(MsSqlAnalysisContext context)
        => context.Statements
            .GroupBy(s => s.Location.File, StringComparer.Ordinal)
            .Select(Build);

    /// <summary>Direct children of a control-flow wrapper, in script order.</summary>
    public IReadOnlyList<SqlStatementInfo> Children(SqlStatementInfo wrapper) => _children[wrapper].ToList();

    /// <summary>Every statement (any depth) inside the TRY body of a <see cref="TryCatchStatement"/> wrapper.</summary>
    public IReadOnlyList<SqlStatementInfo> TryBody(SqlStatementInfo tryCatch)
        => Descendants(_children[tryCatch].Where(c => c.InTryBlock));

    /// <summary>Every statement (any depth) inside the CATCH body of a <see cref="TryCatchStatement"/> wrapper.</summary>
    public IReadOnlyList<SqlStatementInfo> CatchBody(SqlStatementInfo tryCatch)
        => Descendants(_children[tryCatch].Where(c => c.InCatchBlock));

    /// <summary>
    /// The TRY-CATCH wrappers enclosing a statement, innermost first. An error raised by the
    /// statement is handled by the first CATCH; if that CATCH rethrows, by the next one, and so on.
    /// </summary>
    public static IEnumerable<SqlStatementInfo> EnclosingTryCatches(SqlStatementInfo statement)
    {
        for (var ancestor = statement.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.Ast is TryCatchStatement)
            {
                yield return ancestor;
            }
        }
    }

    /// <summary>
    /// Whether the statement sits in the THEN branch of an <c>IF</c> — or the body of a
    /// <c>WHILE</c> — whose predicate reads <c>@@TRANCOUNT</c> or <c>XACT_STATE()</c>: the idioms
    /// that make a COMMIT / ROLLBACK safe to run when no transaction is open
    /// (<c>IF @@TRANCOUNT &gt; 0 ROLLBACK</c>, <c>WHILE @@TRANCOUNT &gt; 0 ROLLBACK</c>).
    /// </summary>
    public static bool IsTranCountGuarded(SqlStatementInfo statement)
    {
        var current = statement;
        for (var ancestor = statement.Parent; ancestor is not null; current = ancestor, ancestor = ancestor.Parent)
        {
            switch (ancestor.Ast)
            {
                case IfStatement { Predicate: { } predicate }
                    when !current.InElseBranch && ReadsTransactionState(predicate):
                case WhileStatement { Predicate: { } loopPredicate }
                    when ReadsTransactionState(loopPredicate):
                    return true;
            }
        }

        return false;
    }

    /// <summary>Whether a boolean expression mentions <c>@@TRANCOUNT</c> or calls <c>XACT_STATE()</c>.</summary>
    public static bool ReadsTransactionState(TSqlFragment predicate)
    {
        var visitor = new TransactionStateVisitor();
        predicate.Accept(visitor);
        return visitor.Found;
    }

    /// <summary>
    /// Whether a CATCH body (or any statement list) rethrows: <c>THROW</c>, <c>RAISERROR</c> with a
    /// literal severity of 11 or more or a non-literal severity (the pre-2012 idiom passes
    /// <c>ERROR_SEVERITY()</c> through a variable), or a call to a procedure whose name says it
    /// raises / throws (<c>usp_RethrowError</c>).
    /// </summary>
    public static bool Rethrows(IEnumerable<SqlStatementInfo> statements)
        => statements.Any(s => s.Ast switch
        {
            ThrowStatement => true,
            RaiseErrorStatement raise => RaiseErrorSeverity(raise) is null or >= 11,
            _ => StatementClassifier.GetProcedureName(s.Ast) is { } name
                && (name.Contains("throw", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("raise", StringComparison.OrdinalIgnoreCase)),
        });

    /// <summary>Literal severity of a <c>RAISERROR</c>; <c>null</c> when it is a variable or expression.</summary>
    public static int? RaiseErrorSeverity(RaiseErrorStatement raise)
        => raise.SecondParameter is IntegerLiteral literal
            && int.TryParse(literal.Value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var severity)
            ? severity
            : null;

    private IReadOnlyList<SqlStatementInfo> Descendants(IEnumerable<SqlStatementInfo> roots)
    {
        var result = new List<SqlStatementInfo>();
        foreach (var root in roots)
        {
            result.Add(root);
            result.AddRange(Descendants(_children[root]));
        }

        return result;
    }

    /// <summary>
    /// Walks one statement list in order. A <c>RETURN</c> / <c>THROW</c> ends the list — whatever
    /// follows is unreachable on this path (a <c>label:</c> handler reached only by <c>GOTO</c>
    /// is the error path). A <c>GOTO</c> whose label is later in the same list jumps to it; one
    /// whose label is earlier is a loop already walked once; one whose label is elsewhere leaves
    /// the list.
    /// </summary>
    private static void WalkList(
        IEnumerable<SqlStatementInfo> statements,
        ILookup<SqlStatementInfo, SqlStatementInfo> children,
        State state,
        Collector collector)
    {
        var list = statements as IReadOnlyList<SqlStatementInfo> ?? statements.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var statement = list[i];
            switch (statement.Ast)
            {
                case ReturnStatement or ThrowStatement:
                    return;

                case GoToStatement go:
                    var label = IndexOfLabel(list, go.LabelName?.Value);
                    if (label < 0)
                    {
                        return;
                    }

                    if (label > i)
                    {
                        i = label;
                    }

                    continue;
            }

            Walk(statement, children, state, collector);
        }
    }

    private static int IndexOfLabel(IReadOnlyList<SqlStatementInfo> list, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return -1;
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Ast is LabelStatement label
                && string.Equals(label.Value?.TrimEnd(':'), name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static void Walk(
        SqlStatementInfo statement,
        ILookup<SqlStatementInfo, SqlStatementInfo> children,
        State state,
        Collector collector)
    {
        switch (statement.Ast)
        {
            case BeginTransactionStatement:
                state.Open.Add(statement);
                break;

            case SaveTransactionStatement save when NameOf(save) is { } savepoint:
                state.Savepoints.Add(savepoint);
                break;

            case CommitTransactionStatement:
                if (state.Open.Count > 0)
                {
                    var begin = state.Open[^1];
                    state.Open.RemoveAt(state.Open.Count - 1);
                    collector.Closed.Add(new ClosedTransaction(begin, statement));
                }
                else if (!IsTranCountGuarded(statement))
                {
                    collector.Unmatched.Add(statement);
                }

                break;

            case RollbackTransactionStatement rollback:
                if (NameOf(rollback) is { } name && state.Savepoints.Contains(name))
                {
                    break; // ROLLBACK TRAN savepoint: the transaction stays open
                }

                if (state.Open.Count > 0)
                {
                    // ROLLBACK ends every level: pair it with each open BEGIN, innermost first.
                    for (var i = state.Open.Count - 1; i >= 0; i--)
                    {
                        collector.Closed.Add(new ClosedTransaction(state.Open[i], statement));
                    }

                    state.Open.Clear();
                }
                else if (!IsTranCountGuarded(statement))
                {
                    collector.Unmatched.Add(statement);
                }

                break;

            case IfStatement:
                WalkIf(statement, children, state, collector);
                break;

            case BeginEndBlockStatement or WhileStatement:
                WalkList(children[statement], children, state, collector);
                break;

            case TryCatchStatement:
                // The CATCH body is the error path; only the TRY body is on the main path.
                WalkList(children[statement].Where(c => c.InTryBlock), children, state, collector);
                break;
        }
    }

    private static void WalkIf(
        SqlStatementInfo ifStatement,
        ILookup<SqlStatementInfo, SqlStatementInfo> children,
        State state,
        Collector collector)
    {
        var thenBranch = children[ifStatement].Where(c => !c.InElseBranch).ToList();
        var elseBranch = children[ifStatement].Where(c => c.InElseBranch).ToList();

        var thenState = state.Clone();
        WalkList(thenBranch, children, thenState, collector);
        var elseState = state.Clone();
        WalkList(elseBranch, children, elseState, collector);

        var candidates = new List<State>(2);
        if (!LeavesScript(thenBranch, children))
        {
            candidates.Add(thenState);
        }

        if (!LeavesScript(elseBranch, children))
        {
            candidates.Add(elseState);
        }

        // Continue with the branch that did something to the transaction state — so a
        // conditional BEGIN TRAN counts as opened and a conditional COMMIT as closed (an empty
        // ELSE never wins). Two differing changes: keep the one with more open transactions.
        // Both branches leave the script: whatever follows is unreachable; keep the THEN state.
        var changed = candidates.Where(c => !c.SameOpenAs(state)).ToList();
        var continuation = changed.Count > 0
            ? changed.MaxBy(c => c.Open.Count)!
            : candidates.Count > 0 ? candidates[0] : thenState;

        state.CopyFrom(continuation);
    }

    /// <summary>A branch whose own statements (looking through BEGIN…END) RETURN, THROW or GOTO does not fall through.</summary>
    private static bool LeavesScript(
        IEnumerable<SqlStatementInfo> branch,
        ILookup<SqlStatementInfo, SqlStatementInfo> children)
        => branch.Any(s => s.Ast is ReturnStatement or ThrowStatement or GoToStatement
            || (s.Ast is BeginEndBlockStatement && LeavesScript(children[s], children)));

    /// <summary>The literal name of a BEGIN / SAVE / COMMIT / ROLLBACK TRANSACTION, as written; <c>null</c> when absent or a variable.</summary>
    public static string? TransactionName(TransactionStatement transaction)
        => transaction.Name?.Identifier?.Value
            ?? (transaction.Name?.ValueExpression as StringLiteral)?.Value;

    private static string? NameOf(TransactionStatement transaction)
        => TransactionName(transaction)?.ToLowerInvariant();

    private sealed class State
    {
        public List<SqlStatementInfo> Open { get; } = [];
        public HashSet<string> Savepoints { get; } = new(StringComparer.Ordinal);

        public bool SameOpenAs(State other) => Open.SequenceEqual(other.Open);

        public State Clone()
        {
            var clone = new State();
            clone.CopyFrom(this);
            return clone;
        }

        public void CopyFrom(State other)
        {
            Open.Clear();
            Open.AddRange(other.Open);
            Savepoints.Clear();
            Savepoints.UnionWith(other.Savepoints);
        }
    }

    private sealed class Collector
    {
        public List<ClosedTransaction> Closed { get; } = [];
        public List<SqlStatementInfo> Unmatched { get; } = [];
    }

    private sealed class TransactionStateVisitor : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(GlobalVariableExpression node)
        {
            if (string.Equals(node.Name, "@@TRANCOUNT", StringComparison.OrdinalIgnoreCase))
            {
                Found = true;
            }
        }

        public override void Visit(FunctionCall node)
        {
            if (string.Equals(node.FunctionName?.Value, "XACT_STATE", StringComparison.OrdinalIgnoreCase))
            {
                Found = true;
            }
        }
    }
}
