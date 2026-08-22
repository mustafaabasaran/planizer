using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql.Rules.Locking;

/// <summary>
/// MSSQL-LOCK-009: UPDATE or DELETE without a WHERE clause and without TOP touches every row;
/// once roughly 5000 row locks accumulate, lock escalation converts them into a table lock and
/// blocks all concurrent access. The fix suggests a WHILE + TOP batching template.
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

            // Table variables / temp tables never escalate locks on user tables, and a JOIN bounds
            // the write even without WHERE.
            if (spec is null || !DmlTargets.IsUnboundedPersistentWrite(spec))
            {
                continue;
            }

            var table = TargetName(spec);
            yield return CreateFinding(statement, DefaultSeverity,
                $"{verb} on {table} has no WHERE and no TOP: it touches every row, and after ~5000 row locks lock escalation turns it into a table lock.",
                BatchingFix(verb, table));
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
