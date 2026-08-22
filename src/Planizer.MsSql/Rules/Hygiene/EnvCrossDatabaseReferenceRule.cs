using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Rules.Failure;

namespace Planizer.MsSql.Rules.Hygiene;

/// <summary>
/// MSSQL-ENV-002: names that reach outside the current database tie the script to one
/// environment. A four-part name (<c>server.db.schema.object</c>) needs a linked server and
/// runs as a distributed query — Warning per statement. A three-part name
/// (<c>db.schema.object</c>, e.g. <c>[LookupDb].dbo.X</c>) needs that database to
/// exist under exactly that name — one Info per file (ADR-0001 pattern) with the count and
/// first examples. The system databases (master, tempdb, msdb, model) exist on every instance
/// and are ignored. Module bodies are inspected too: a view or procedure that binds to another
/// database is exactly the coupling this rule reports. Control-flow wrappers contribute only
/// their own predicate (<see cref="StatementScan.OwnFragments"/>), so a name nested in
/// <c>IF … BEGIN … END</c> is counted once, for the statement that contains it.
/// </summary>
public sealed class EnvCrossDatabaseReferenceRule : MsSqlRuleBase
{
    private const int ExampleCount = 3;

    private static readonly HashSet<string> SystemDatabases =
        new(StringComparer.OrdinalIgnoreCase) { "master", "tempdb", "msdb", "model" };

    public override string Id => "MSSQL-ENV-002";
    public override string Title => "Linked-server or cross-database reference ties the script to one environment";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        var crossDatabaseByFile = new Dictionary<string, List<(SqlStatementInfo Statement, ExternalName Name)>>(StringComparer.Ordinal);

        foreach (var statement in context.Statements)
        {
            var visitor = new ExternalNameVisitor();
            foreach (var fragment in StatementScan.OwnFragments(statement))
            {
                fragment.Accept(visitor);
            }

            var linked = visitor.Names.Where(n => n.Server is not null).ToList();
            if (linked.Count > 0)
            {
                var servers = linked.Select(n => n.Server!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                yield return CreateFinding(statement, Severity.Warning,
                    $"`{DescribeStatement(statement)}` references linked server " +
                    $"{string.Join(", ", servers)} ({linked[0].Display}): the script only works where that " +
                    "linked server is configured, and the remote access runs as a distributed query.",
                    "Take the linked-server dependency out of the migration: stage the remote data in a local " +
                    "table beforehand, or reach it through a synonym that is created per environment.");
            }

            if (statement.SuppressedRuleIds.Contains(Id))
            {
                continue;
            }

            foreach (var name in visitor.Names.Where(n => n.Server is null))
            {
                if (!crossDatabaseByFile.TryGetValue(statement.Location.File, out var list))
                {
                    crossDatabaseByFile[statement.Location.File] = list = [];
                }

                list.Add((statement, name));
            }
        }

        foreach (var references in crossDatabaseByFile.Values)
        {
            var statementCount = references.Select(r => r.Statement.Index).Distinct().Count();
            var databases = references
                .Select(r => r.Name.Database!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var examples = references
                .Select(r => r.Name.Display)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(ExampleCount);

            yield return CreateFinding(references[0].Statement, Severity.Info,
                $"{statementCount} statement{(statementCount == 1 ? "" : "s")} in this file " +
                $"reference{(statementCount == 1 ? "s" : "")} {databases.Count} other " +
                $"database{(databases.Count == 1 ? "" : "s")} by name ({string.Join(", ", databases)}), " +
                $"e.g. {string.Join(", ", examples)}: the script only runs where " +
                $"{(databases.Count == 1 ? "that database exists" : "those databases exist")} under exactly " +
                $"{(databases.Count == 1 ? "that name" : "those names")}.",
                "Reference other databases through synonyms created per environment " +
                "(CREATE SYNONYM dbo.X FOR [OtherDb].dbo.X), or move the dependency out of the migration.");
        }
    }

    /// <summary>A name that leaves the current database; <see cref="Server"/> is set for four-part names.</summary>
    private sealed record ExternalName(string? Server, string? Database, string Display);

    private sealed class ExternalNameVisitor : TSqlFragmentVisitor
    {
        public List<ExternalName> Names { get; } = [];

        public override void Visit(SchemaObjectName name)
        {
            if (name.ServerIdentifier is { Value.Length: > 0 } server)
            {
                Names.Add(new ExternalName(server.Value, name.DatabaseIdentifier?.Value, Render(name.Identifiers)));
            }
            else if (name.DatabaseIdentifier is { Value.Length: > 0 } database && !SystemDatabases.Contains(database.Value))
            {
                Names.Add(new ExternalName(null, database.Value, Render(name.Identifiers)));
            }
        }

        /// <summary>
        /// A scalar function call (<c>OtherDb.dbo.fn(…)</c>) keeps everything before the function name
        /// in a bare multi-part call target: two parts = database.schema, three = server.database.schema.
        /// </summary>
        public override void Visit(FunctionCall call)
        {
            if (call.CallTarget is not MultiPartIdentifierCallTarget { MultiPartIdentifier.Identifiers: { } parts })
            {
                return;
            }

            var display = $"{Render(parts)}.{call.FunctionName?.Value ?? "<function>"}";
            if (parts.Count == 3 && parts[0].Value.Length > 0)
            {
                Names.Add(new ExternalName(parts[0].Value, parts[1].Value, display));
            }
            else if (parts.Count == 2 && parts[0].Value.Length > 0 && !SystemDatabases.Contains(parts[0].Value))
            {
                Names.Add(new ExternalName(null, parts[0].Value, display));
            }
        }

        private static string Render(IList<Identifier> identifiers)
            => string.Join(".", identifiers.Select(i => i.Value));
    }
}
