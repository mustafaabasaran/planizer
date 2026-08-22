using Planizer.Core;

namespace Planizer.MsSql;

/// <summary>
/// Base class for every MSSQL rule. Concrete subclasses are discovered by
/// <see cref="MsSqlAnalyzer"/> via reflection — no central registry file to edit.
/// </summary>
public abstract class MsSqlRuleBase : IRule
{
    private MsSqlAnalysisContext? _context;

    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract Severity DefaultSeverity { get; }

    public IEnumerable<Finding> Analyze(IAnalysisContext context)
    {
        var msSqlContext = (MsSqlAnalysisContext)context;
        _context = msSqlContext;
        return Analyze(msSqlContext);
    }

    protected abstract IEnumerable<Finding> Analyze(MsSqlAnalysisContext context);

    /// <summary>Builds a finding anchored to a statement; fills Location, StatementSummary and Assumption.</summary>
    protected Finding CreateFinding(
        SqlStatementInfo statement,
        Severity severity,
        string message,
        string? fix = null,
        bool inconclusive = false)
    {
        var context = _context
            ?? throw new InvalidOperationException("CreateFinding may only be called from within Analyze.");

        return new Finding
        {
            RuleId = Id,
            Severity = severity,
            Message = message,
            Fix = fix,
            Location = statement.Location,
            StatementSummary = Summarize(statement.Sql),
            Assumption = context.AssumptionText,
            Inconclusive = inconclusive,
        };
    }

    /// <summary>First ~120 characters of the statement, whitespace collapsed.</summary>
    private static string Summarize(string sql) => Summarize(sql, 120, ellipsis: false);

    /// <summary>
    /// Compact, single-line rendering of the statement for use inside a message (default 80
    /// characters, "…" when cut) so two findings on similar statements read differently.
    /// </summary>
    protected static string DescribeStatement(SqlStatementInfo statement, int maxLength = 80)
        => Summarize(statement.Sql, maxLength, ellipsis: true);

    private static string Summarize(string sql, int maxLength, bool ellipsis)
    {
        var collapsed = string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length <= maxLength)
        {
            return collapsed;
        }

        return ellipsis ? collapsed[..(maxLength - 1)] + "\u2026" : collapsed[..maxLength];
    }
}
