using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql;

/// <summary>
/// Decides whether a DEFAULT expression is a <em>runtime constant</em> — the requirement for the
/// metadata-only ADD COLUMN NOT NULL fast path on Enterprise/Azure. Per Microsoft's ALTER TABLE
/// documentation the bar is "evaluated once at the start of the statement, regardless of
/// determinism": literals, CAST/CONVERT of runtime constants, and statement-level functions such
/// as GETDATE(), SYSDATETIME() or CURRENT_TIMESTAMP all qualify and stay metadata-only. Only
/// functions evaluated per row — NEWID(), NEWSEQUENTIALID() — break the fast path on every
/// edition. Unrecognized functions are treated as non-constant conservatively; the planned
/// Docker validation pass is the tie-breaker for that bucket.
/// </summary>
internal static class DefaultExpressionClassifier
{
    /// <summary>
    /// Statement-level functions: evaluated once per statement, hence runtime constants even
    /// though several of them are non-deterministic.
    /// </summary>
    private static readonly HashSet<string> StatementLevelFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "GETDATE", "GETUTCDATE", "SYSDATETIME", "SYSUTCDATETIME", "SYSDATETIMEOFFSET",
        "CURRENT_TIMESTAMP", "SUSER_SNAME", "SUSER_NAME", "USER_NAME", "SESSION_USER",
        "ORIGINAL_LOGIN",
    };

    /// <summary>Functions evaluated for every row: these break the metadata-only fast path.</summary>
    private static readonly HashSet<string> PerRowFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "NEWID", "NEWSEQUENTIALID",
    };

    /// <summary>Whether the expression is a runtime constant (metadata-only fast path eligible).</summary>
    public static bool IsRuntimeConstant(ScalarExpression expression) => expression switch
    {
        Literal => true,
        UnaryExpression unary => IsRuntimeConstant(unary.Expression),
        ParenthesisExpression paren => IsRuntimeConstant(paren.Expression),
        CastCall cast => IsRuntimeConstant(cast.Parameter),
        ConvertCall convert => IsRuntimeConstant(convert.Parameter),
        // CURRENT_TIMESTAMP, SESSION_USER, USER … — parameterless, once per statement.
        ParameterlessCall => true,
        FunctionCall { CallTarget: null, Parameters.Count: 0, FunctionName.Value: { } name }
            => StatementLevelFunctions.Contains(name),
        _ => false,
    };

    /// <summary>Whether the expression is a call of a known per-row function (NEWID and friends).</summary>
    public static bool IsPerRowFunction(ScalarExpression expression) => expression switch
    {
        ParenthesisExpression paren => IsPerRowFunction(paren.Expression),
        FunctionCall { FunctionName.Value: { } name } => PerRowFunctions.Contains(name),
        _ => false,
    };

    /// <summary>"NEWID()" for a recognized function call; a generic rendering otherwise.</summary>
    public static string? DescribeFunction(ScalarExpression expression) => expression switch
    {
        ParenthesisExpression paren => DescribeFunction(paren.Expression),
        FunctionCall { FunctionName.Value: { } name }
            => PerRowFunctions.Contains(name) ? $"{name.ToUpperInvariant()}()" : $"{name}()",
        _ => null,
    };
}
