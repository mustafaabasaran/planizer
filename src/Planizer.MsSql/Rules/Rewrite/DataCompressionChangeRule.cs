using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-012: changing DATA_COMPRESSION (ALTER TABLE … REBUILD or ALTER INDEX … REBUILD with
/// a DATA_COMPRESSION option) rewrites the entire table or index. The behavior catalog carries
/// the edition/version matrix: no applicable row means compression is not available on this
/// target at all (Standard/Express before 2016 SP1) and the statement will simply fail — that
/// case escalates to Blocker.
/// </summary>
public sealed class DataCompressionChangeRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-RW-012";
    public override string Title => "Changing DATA_COMPRESSION rewrites the whole table or index";
    public override Severity DefaultSeverity => Severity.Critical;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            var target = statement.Ast switch
            {
                AlterTableRebuildStatement rebuild when HasDataCompression(rebuild.IndexOptions)
                    => $"table {SqlNames.Render(rebuild.SchemaObjectName, "the table")}",
                AlterIndexStatement { AlterIndexType: AlterIndexType.Rebuild } alter
                    when HasDataCompression(alter.IndexOptions)
                    => $"index {alter.Name?.Value ?? "the index"} on " +
                       SqlNames.Render(alter.OnName, "the table"),
                _ => null,
            };

            if (target is null)
            {
                continue;
            }

            var behavior = context.Catalog.Lookup(
                DdlOperationKeys.DataCompressionChange, context.Config.TargetVersion, context.Config.Edition);

            if (behavior is null)
            {
                // No catalog row applies: on this edition/version compression is unavailable
                // (Enterprise-only before 2016 SP1) and the statement fails with error 7738.
                yield return CreateFinding(statement, Severity.Blocker,
                    $"DATA_COMPRESSION is not available on this edition before SQL Server 2016 SP1; " +
                    $"rebuilding {target} with compression will fail.",
                    fix: "Remove the DATA_COMPRESSION option, or correct the target version/edition " +
                         "assumption if the server actually supports compression.");
            }
            else
            {
                yield return CreateFinding(statement, DefaultSeverity,
                    $"Changing DATA_COMPRESSION fully rewrites {target} while holding a Sch-M lock.",
                    fix: "Combine with ONLINE = ON (Enterprise) or run in a maintenance window; " +
                         "compress the largest partitions/indexes one at a time.");
            }
        }
    }

    private static bool HasDataCompression(IEnumerable<IndexOption> options)
        => options.OfType<DataCompressionOption>().Any();
}
