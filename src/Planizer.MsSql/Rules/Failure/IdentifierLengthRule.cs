using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Failure;

/// <summary>
/// MSSQL-LIM-002: a name longer than SQL Server accepts (error 103 at execution). Regular
/// identifiers are capped at 128 characters — ScriptDom already rejects longer ones (error
/// 46095 → MSSQL-PARSE-001) — but it accepts two things the server refuses: local temporary
/// table names over 116 characters (the <c>#</c> counts; the server needs the remaining 12 for
/// its per-session uniquifying suffix — global <c>##</c> tables carry no suffix and get the full
/// 128) and variable names over 128 characters (the <c>@</c> counts). Module bodies are scanned
/// too, because the failure happens at CREATE time.
/// </summary>
public sealed class IdentifierLengthRule : MsSqlRuleBase
{
    private const int MaxIdentifierLength = 128;
    private const int MaxTempTableNameLength = 116;

    public override string Id => "MSSQL-LIM-002";
    public override string Title => "Identifier longer than SQL Server allows";
    public override Severity DefaultSeverity => Severity.Blocker;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            var collector = new NameCollector();
            foreach (var fragment in StatementScan.OwnFragments(statement))
            {
                fragment.Accept(collector);
            }

            foreach (var name in collector.Names)
            {
                var (kind, limit) = name switch
                {
                    ['#', '#', ..] => ("Global temporary table name", MaxIdentifierLength),
                    ['#', ..] => ("Temporary table name", MaxTempTableNameLength),
                    ['@', ..] => ("Variable name", MaxIdentifierLength),
                    _ => ("Identifier", MaxIdentifierLength),
                };

                if (name.Length <= limit)
                {
                    continue;
                }

                yield return CreateFinding(statement, DefaultSeverity,
                    $"{kind} '{Abbreviate(name)}' is {name.Length} characters long; SQL Server allows at most {limit} " +
                    "(error 103 at execution).",
                    $"Shorten the name to {limit} characters or fewer.");
            }
        }
    }

    private static string Abbreviate(string name)
        => name.Length <= 40 ? name : name[..39] + "…";

    /// <summary>Distinct identifier and variable names of a fragment, in first-seen order.</summary>
    private sealed class NameCollector : TSqlFragmentVisitor
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public List<string> Names { get; } = [];

        public override void Visit(Identifier node) => Add(node.Value);

        public override void Visit(VariableReference node) => Add(node.Name);

        private void Add(string? name)
        {
            if (!string.IsNullOrEmpty(name) && _seen.Add(name))
            {
                Names.Add(name);
            }
        }
    }
}
