using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-015: ALTER TABLE … ADD CONSTRAINT PRIMARY KEY / UNIQUE builds the backing index and
/// scans the table to validate uniqueness; the offline index build locking rules apply for the
/// duration (clustered: Sch-M blocks all access; nonclustered: S lock blocks writes). A PRIMARY
/// KEY defaults to clustered, a UNIQUE constraint to nonclustered.
/// </summary>
public sealed class PrimaryKeyOrUniqueConstraintRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-RW-015";
    public override string Title => "Adding a PRIMARY KEY/UNIQUE constraint builds an index with a uniqueness scan";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (statement.Ast is not AlterTableAddTableElementStatement add)
            {
                continue;
            }

            var table = SqlNames.Render(add.SchemaObjectName, "the table");

            foreach (var constraint in add.Definition?.TableConstraints ?? [])
            {
                if (constraint is not UniqueConstraintDefinition unique)
                {
                    continue;
                }

                var kind = unique.IsPrimaryKey ? "PRIMARY KEY" : "UNIQUE";
                var name = unique.ConstraintIdentifier?.Value ?? $"the {kind} constraint";
                var clustered = unique.Clustered ?? unique.IsPrimaryKey; // PK defaults to clustered
                var lockNote = clustered
                    ? "a clustered index build holds a Sch-M lock on the table: all access is blocked"
                    : "an offline nonclustered index build holds an S lock on the table: writes are blocked";

                var behavior = context.Catalog.Lookup(
                    DdlOperationKeys.AddPkOrUnique, context.Config.TargetVersion, context.Config.Edition);

                if (behavior is null)
                {
                    yield return CreateFinding(statement, Severity.Warning,
                        $"Adding {kind} constraint {name} on {table}: behavior is not cataloged for " +
                        "this target — review manually.",
                        inconclusive: true);
                }
                else
                {
                    yield return CreateFinding(statement, DefaultSeverity,
                        $"Adding {kind} constraint {name} builds a {(clustered ? "clustered" : "nonclustered")} " +
                        $"index on {table} and scans it to validate uniqueness; {lockNote} for the " +
                        "duration of the build.");
                }
            }
        }
    }
}
