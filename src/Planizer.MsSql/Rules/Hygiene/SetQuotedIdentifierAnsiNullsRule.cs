using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Rules.Rewrite;

namespace Planizer.MsSql.Rules.Hygiene;

/// <summary>
/// MSSQL-SET-001: a filtered index or a PERSISTED computed column can only be created while
/// <c>QUOTED_IDENTIFIER</c> and <c>ANSI_NULLS</c> are ON; otherwise the statement fails with
/// error 1934. The SET state is tracked per file in script order (it survives <c>GO</c> — the
/// options belong to the session). An explicit OFF still in force is a Blocker; no explicit
/// setting at all is a Warning, because the outcome then depends on the client: sqlcmd/osql run
/// with QUOTED_IDENTIFIER OFF unless <c>-I</c> is passed. An explicit ON keeps the rule quiet.
/// </summary>
public sealed class SetQuotedIdentifierAnsiNullsRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-SET-001";
    public override string Title => "Filtered index / PERSISTED computed column needs QUOTED_IDENTIFIER and ANSI_NULLS ON";
    public override Severity DefaultSeverity => Severity.Blocker;

    private const string FixText = "Put these two lines at the top of the script (and do not switch them OFF later):\n" +
                                   "SET QUOTED_IDENTIFIER ON;\n" +
                                   "SET ANSI_NULLS ON;";

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var file in context.Statements.Select(s => s.Location.File).Distinct(StringComparer.Ordinal))
        {
            var quotedIdentifier = new OptionState("QUOTED_IDENTIFIER");
            var ansiNulls = new OptionState("ANSI_NULLS");

            foreach (var statement in context.StatementsInFile(file))
            {
                if (statement.Ast is PredicateSetStatement set)
                {
                    Track(set, SetOptions.QuotedIdentifier, quotedIdentifier, statement);
                    Track(set, SetOptions.AnsiNulls, ansiNulls, statement);
                    continue;
                }

                foreach (var what in RequiringConstructs(statement.Ast))
                {
                    var off = new[] { quotedIdentifier, ansiNulls }.Where(o => o.IsOn == false).ToList();
                    if (off.Count > 0)
                    {
                        yield return CreateFinding(statement, Severity.Blocker,
                            $"{what} requires {Names(off)} ON, but this script switched " +
                            $"{Names(off)} OFF earlier ({Where(off)}): the statement fails with error 1934.",
                            FixText);
                        continue;
                    }

                    var unknown = new[] { quotedIdentifier, ansiNulls }.Where(o => o.IsOn is null).ToList();
                    if (unknown.Count > 0)
                    {
                        var clientNote = unknown.Contains(quotedIdentifier)
                            ? "sqlcmd and osql run with QUOTED_IDENTIFIER OFF unless -I is given, and the statement then fails with error 1934"
                            : "a legacy driver or an explicit OFF on the connection makes the statement fail with error 1934";

                        yield return CreateFinding(statement, Severity.Warning,
                            $"{what} requires {Names(unknown)} ON; the script never sets " +
                            $"{Names(unknown)} explicitly, so the outcome depends on the connection defaults — {clientNote}.",
                            FixText,
                            inconclusive: true);
                    }
                }
            }
        }
    }

    private static void Track(PredicateSetStatement set, SetOptions option, OptionState state, SqlStatementInfo statement)
    {
        // SET ANSI_DEFAULTS ON|OFF flips ANSI_NULLS and QUOTED_IDENTIFIER together.
        if (set.Options.HasFlag(option) || set.Options.HasFlag(SetOptions.AnsiDefaults))
        {
            state.IsOn = set.IsOn;
            state.Line = statement.Location.Line;
        }
    }

    /// <summary>Human descriptions of every construct in the statement that needs the options ON.</summary>
    private static IEnumerable<string> RequiringConstructs(TSqlStatement statement)
    {
        switch (statement)
        {
            case CreateIndexStatement { FilterPredicate: not null } index:
                yield return $"Filtered index {index.Name?.Value ?? "(unnamed)"} on {SqlNames.Table(index.OnName)}";
                break;

            case AlterTableAddTableElementStatement add:
                foreach (var description in PersistedComputedColumns(add.Definition, add.SchemaObjectName))
                {
                    yield return description;
                }

                break;

            case CreateTableStatement create:
                foreach (var description in PersistedComputedColumns(create.Definition, create.SchemaObjectName))
                {
                    yield return description;
                }

                foreach (var inline in create.Definition?.Indexes ?? [])
                {
                    if (inline.FilterPredicate is not null)
                    {
                        yield return $"Filtered index {inline.Name?.Value ?? "(unnamed)"} on {SqlNames.Table(create.SchemaObjectName)}";
                    }
                }

                break;
        }
    }

    private static IEnumerable<string> PersistedComputedColumns(TableDefinition? definition, SchemaObjectName? table)
    {
        foreach (var column in definition?.ColumnDefinitions ?? [])
        {
            if (column.ComputedColumnExpression is not null && column.IsPersisted)
            {
                yield return $"PERSISTED computed column {column.ColumnIdentifier?.Value ?? "(unnamed)"} on {SqlNames.Table(table)}";
            }
        }
    }

    private static string Names(IReadOnlyList<OptionState> options)
        => string.Join(" and ", options.Select(o => o.Name));

    private static string Where(IReadOnlyList<OptionState> options)
        => string.Join(", ", options.Select(o => $"{o.Name} at line {o.Line}"));

    /// <summary>Last explicit state of one SET option while walking a file; <c>null</c> = never set.</summary>
    private sealed class OptionState(string name)
    {
        public string Name { get; } = name;
        public bool? IsOn { get; set; }
        public int Line { get; set; }
    }
}
