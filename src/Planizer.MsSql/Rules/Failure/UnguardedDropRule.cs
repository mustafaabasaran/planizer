using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Failure;

/// <summary>
/// MSSQL-IDEM-003: DROP TABLE / INDEX / VIEW / PROCEDURE / FUNCTION / TRIGGER / TYPE / SEQUENCE /
/// SCHEMA without <c>IF EXISTS</c> and without a catalog-querying enclosing IF. The first run
/// succeeds; a second run — or a first run on an environment that never had the object — fails
/// with "Cannot drop … because it does not exist or you do not have permission" (3701; 15151 for a
/// schema). An object the same file created earlier — <c>CREATE …</c> or <c>SELECT … INTO</c> —
/// is exempt: a re-run recreates it first. An exit guard earlier in the batch
/// (<c>IF OBJECT_ID(…) IS NULL RETURN;</c>) counts as a catalog check.
/// </summary>
public sealed class UnguardedDropRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-IDEM-003";
    public override string Title => "DROP without IF EXISTS is not re-runnable";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            var dropped = IdempotencyTargets.Dropped(statement.Ast)
                .Where(d => !d.IsIfExists)
                .Select(d => d.Object)
                .ToList();

            if (dropped.Count == 0 || IdempotencyGuard.IsGuarded(statement, context))
            {
                continue;
            }

            dropped.RemoveAll(d => CreatedEarlierInFile(statement, context, d));
            if (dropped.Count == 0)
            {
                continue;
            }

            var first = dropped[0];
            var names = string.Join(", ", dropped.Select(d => d.Display));
            var subject = dropped.Count == 1 ? $"the {first.Kind} is" : $"the {Plural(first.Kind)} are";

            yield return CreateFinding(statement, DefaultSeverity,
                $"{first.Keyword} {names} is not guarded by an existence check; " +
                $"running the script when {subject} already gone fails (error {(first.Kind == "schema" ? "15151" : "3701")}).",
                Fix(statement, dropped, context.Config.TargetVersion));
        }
    }

    /// <summary>CREATE … or SELECT … INTO of the same object earlier in the file (the staging pattern).</summary>
    private static bool CreatedEarlierInFile(SqlStatementInfo statement, MsSqlAnalysisContext context, SchemaObject dropped)
        => IdempotencyTargets.EarlierInFile(statement, context)
            .Select(earlier => IdempotencyTargets.Created(earlier.Ast)?.Object ?? IdempotencyTargets.SelectedInto(earlier.Ast))
            .Any(created => created is not null && created.SameAs(dropped));

    private static string Plural(string kind) => kind.EndsWith('x') ? kind + "es" : kind + "s";

    private static string Fix(SqlStatementInfo statement, IReadOnlyList<SchemaObject> dropped, SqlServerVersion target)
    {
        var first = dropped[0];

        if (IdempotencyTargets.SupportsDropIfExists(target))
        {
            var objects = first.Kind switch
            {
                "index" => string.Join(", ", dropped.Select(d => $"{d.Display} ON {IdempotencyTargets.Display(d.Parent)}")),
                _ => string.Join(", ", dropped.Select(d => d.Display)),
            };

            var scope = first.TriggerScope switch
            {
                TriggerScope.Database => " ON DATABASE",
                TriggerScope.AllServer => " ON ALL SERVER",
                _ => "",
            };

            return $"Use {first.Keyword} IF EXISTS {objects}{scope};";
        }

        var guard = string.Join(" AND ", dropped.Select(d => d.ExistsPredicate()));
        return $"Guard it (DROP … IF EXISTS needs SQL Server 2016): IF {guard} {IdempotencyTargets.Collapse(statement.Sql)}";
    }
}
