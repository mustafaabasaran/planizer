using Planizer.Core;

namespace Planizer.MsSql.Rules.Dynamic;

/// <summary>
/// MSSQL-DYN-001: dynamic SQL — <c>EXEC('…')</c>, <c>EXEC @sql</c>, <c>sp_executesql</c> — is a
/// blind spot for every static rule: it can contain any DDL or DML without being analyzed. The
/// classifier marks these statements <see cref="StatementKind.Dynamic"/> (they also feed
/// <c>ScriptSummary.UnanalyzableCount</c>); this rule surfaces each one for manual review.
/// </summary>
public sealed class DynamicSqlRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-DYN-001";
    public override string Title => "Dynamic SQL cannot be analyzed statically";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
        => context.Statements
            .Where(s => s.Kind == StatementKind.Dynamic)
            .Select(s => CreateFinding(s, Severity.Warning,
                "Dynamic SQL cannot be analyzed statically; review manually."));
}
