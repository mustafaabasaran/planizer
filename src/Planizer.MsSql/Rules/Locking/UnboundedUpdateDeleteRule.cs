using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql.Rules.Locking;

/// <summary>
/// MSSQL-LOCK-009: UPDATE or DELETE without a WHERE clause and without TOP touches every row;
/// once roughly 5000 row locks accumulate, lock escalation converts them into a table lock and
/// blocks all concurrent access. The fix suggests a WHILE + TOP batching template.
/// A join in the FROM clause only counts as a filter when it can actually drop target rows
/// (<see cref="DmlTargets.ClassifyPersistentWrite"/>); where that depends on the data — an INNER
/// JOIN, a CROSS APPLY — the rule reports Info + <c>Inconclusive</c> rather than staying silent.
/// </summary>
public sealed class UnboundedUpdateDeleteRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-LOCK-009";
    public override string Title => "Unbounded UPDATE/DELETE escalates to a table lock";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            var (spec, verb) = statement.Ast switch
            {
                UpdateStatement update => ((UpdateDeleteSpecificationBase)update.UpdateSpecification, "UPDATE"),
                DeleteStatement delete => ((UpdateDeleteSpecificationBase)delete.DeleteSpecification, "DELETE"),
                _ => (null, ""),
            };

            if (spec is null)
            {
                continue;
            }

            // A WHERE clause, a TOP filter, a filtering join, or a table variable / temp table
            // target (which escalates no locks on user tables) all end the analysis here.
            var bounds = DmlTargets.ClassifyPersistentWrite(spec);
            if (bounds.Boundedness == JoinBoundedness.Bounded)
            {
                continue;
            }

            var table = TargetName(spec);
            var fix = BatchingFix(verb, table);

            if (bounds.Boundedness == JoinBoundedness.Inconclusive)
            {
                yield return CreateFinding(statement, Severity.Info,
                    $"{verb} on {table} has no WHERE and no TOP; how many rows it touches depends on " +
                    $"the cardinality of the {bounds.Join}, which may match every row — then ~5000 row " +
                    "locks escalate into a table lock. A schema snapshot settles this.",
                    fix,
                    inconclusive: true);
                continue;
            }

            yield return CreateFinding(statement, DefaultSeverity,
                bounds.Join is null
                    ? $"{verb} on {table} has no WHERE and no TOP: it touches every row, and after " +
                      "~5000 row locks lock escalation turns it into a table lock."
                    : $"{verb} on {table} has no WHERE and no TOP, and the {bounds.Join} does not " +
                      $"restrict {table}: it touches every row, and after ~5000 row locks lock " +
                      "escalation turns it into a table lock.",
                fix);
        }
    }

    private static string TargetName(UpdateDeleteSpecificationBase spec)
        => DmlTargets.ResolveTargetTable(spec) is { } name && name.Identifiers.Count > 0
            ? string.Join(".", name.Identifiers.Select(i => i.Value))
            : "the table";

    private static string BatchingFix(string verb, string table)
        => verb == "DELETE"
            ? "Batch the delete:\n" +
              "WHILE 1 = 1\n" +
              "BEGIN\n" +
              $"    DELETE TOP (4000) FROM {table} WHERE <condition>;\n" +
              "    IF @@ROWCOUNT = 0 BREAK;\n" +
              "END"
            : "Batch the update:\n" +
              "WHILE 1 = 1\n" +
              "BEGIN\n" +
              $"    UPDATE TOP (4000) {table} SET <assignments> WHERE <not-yet-updated condition>;\n" +
              "    IF @@ROWCOUNT = 0 BREAK;\n" +
              "END";
}
