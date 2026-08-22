using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Failure;

/// <summary>
/// MSSQL-LIT-001: a string literal without the <c>N</c> prefix contains characters outside
/// ASCII (<c>'Ödeme Türü'</c>). Such a literal is varchar in the database's default collation:
/// characters outside that collation's code page are replaced by <c>?</c> before the value ever
/// reaches an nvarchar column. Reported once per file (ADR-0001 shape): anchored at the first
/// offending statement, with the count and the first examples. Statements suppressed for this
/// rule leave the count; module bodies are not walked; and message text — the arguments of
/// <c>PRINT</c>, <c>RAISERROR</c> and <c>THROW</c> — is out of scope: it never reaches a column,
/// so a <c>?</c> in the deployment log is cosmetic, not a data risk.
/// </summary>
public sealed class NonUnicodeLiteralRule : MsSqlRuleBase
{
    private const int ExampleCount = 3;
    private const int MaxExampleLength = 40;

    public override string Id => "MSSQL-LIT-001";
    public override string Title => "Non-ASCII string literal without the N prefix";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        var byFile = new Dictionary<string, List<(SqlStatementInfo Statement, StringLiteral Literal)>>(StringComparer.Ordinal);

        foreach (var statement in context.Statements)
        {
            if (statement.SuppressedRuleIds.Contains(Id) || IsOutOfScope(statement.Ast))
            {
                continue;
            }

            var collector = new LiteralCollector();
            foreach (var fragment in StatementScan.OwnFragments(statement))
            {
                fragment.Accept(collector);
            }

            if (collector.Literals.Count == 0)
            {
                continue;
            }

            if (!byFile.TryGetValue(statement.Location.File, out var list))
            {
                byFile[statement.Location.File] = list = [];
            }

            list.AddRange(collector.Literals.Select(l => (statement, l)));
        }

        foreach (var literals in byFile.Values)
        {
            var count = literals.Count;
            var first = literals[0];
            var examples = literals
                .Select(l => l.Literal.Value)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Take(ExampleCount - 1)
                .Select(Quote)
                .ToList();

            var where = count == 1
                ? $": {Quote(first.Literal.Value)} (line {first.Literal.StartLine})"
                : $" (first: {Quote(first.Literal.Value)} at line {first.Literal.StartLine}" +
                  (examples.Count > 0 ? $"; also {string.Join(", ", examples)}" : "") + ")";

            yield return CreateFinding(first.Statement, Severity.Warning,
                $"{count} string literal{(count == 1 ? "" : "s")} in this file " +
                $"contain{(count == 1 ? "s" : "")} non-ASCII characters without the N prefix{where}. " +
                "Without N the literal is varchar in the database's default collation: characters outside " +
                "that collation's code page are replaced by '?' before the value ever reaches an nvarchar column.",
                fix: $"Prefix the literal{(count == 1 ? "" : "s")} with N: N{Quote(first.Literal.Value)}" +
                     (count > 1 ? $" (and the {count - 1} other{(count == 2 ? "" : "s")})" : ""));
        }
    }

    /// <summary>
    /// Module definitions (their bodies are never flattened) and message-text statements: a
    /// <c>PRINT</c> / <c>RAISERROR</c> / <c>THROW</c> argument is shown, never stored.
    /// </summary>
    private static bool IsOutOfScope(TSqlStatement ast)
        => StatementScan.IsModuleDefinition(ast) || ast is PrintStatement or RaiseErrorStatement or ThrowStatement;

    private static string Quote(string value)
    {
        var shown = value.Length <= MaxExampleLength ? value : value[..(MaxExampleLength - 1)] + "…";
        return $"'{shown}'";
    }

    private sealed class LiteralCollector : TSqlFragmentVisitor
    {
        public List<StringLiteral> Literals { get; } = [];

        public override void Visit(StringLiteral node)
        {
            if (!node.IsNational && node.Value is { } value && value.Any(c => c > 127))
            {
                Literals.Add(node);
            }
        }
    }
}
