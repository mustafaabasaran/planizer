using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-011: adding a PERSISTED computed column materializes the value for every existing
/// row — a full scan plus writes under the ALTER TABLE Sch-M lock. A non-persisted computed
/// column is metadata-only and does not trigger this rule.
/// </summary>
public sealed class PersistedComputedColumnRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-RW-011";
    public override string Title => "Adding a PERSISTED computed column scans and writes the whole table";
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

            foreach (var column in add.Definition?.ColumnDefinitions ?? [])
            {
                if (column.ComputedColumnExpression is null || !column.IsPersisted)
                {
                    continue;
                }

                var name = column.ColumnIdentifier?.Value ?? "the computed column";
                var behavior = context.Catalog.Lookup(
                    DdlOperationKeys.AddComputedPersisted, context.Config.TargetVersion, context.Config.Edition);

                if (behavior is null)
                {
                    yield return CreateFinding(statement, Severity.Warning,
                        $"Adding PERSISTED computed column {name} to {table}: behavior is not cataloged " +
                        "for this target — review manually.",
                        inconclusive: true);
                }
                else
                {
                    yield return CreateFinding(statement, DefaultSeverity,
                        $"Adding PERSISTED computed column {name} computes and writes a value for every " +
                        $"row of {table} under a Sch-M lock (full scan plus writes).",
                        fix: $"Add {name} without PERSISTED (metadata-only), and persist it later in a " +
                             "maintenance window only if an index or performance requires it.");
                }
            }
        }
    }
}
