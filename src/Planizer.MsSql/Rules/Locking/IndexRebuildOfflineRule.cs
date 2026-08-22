using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Locking;

/// <summary>
/// MSSQL-LOCK-006: ALTER INDEX … REBUILD without ONLINE = ON holds a schema-modification (Sch-M)
/// lock on the table for the entire rebuild. Enterprise/Azure can rebuild online; every edition
/// can fall back to REORGANIZE, which is always online.
/// </summary>
public sealed class IndexRebuildOfflineRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-LOCK-006";
    public override string Title => "Offline index rebuild blocks all access to the table";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (statement.Ast is not AlterIndexStatement { AlterIndexType: AlterIndexType.Rebuild } rebuild
                || IndexOptionInspector.IsOnline(rebuild.IndexOptions))
            {
                continue;
            }

            var table = IndexOptionInspector.TargetTable(rebuild);
            yield return CreateFinding(statement, DefaultSeverity,
                $"Offline ALTER INDEX REBUILD takes a schema-modification (Sch-M) lock on {table} for the duration of the rebuild; all reads and writes are blocked.",
                Fix(context.Config));
        }
    }

    private static string Fix(PlanizerConfig config)
        => config.Edition is SqlEdition.Enterprise or SqlEdition.Azure
            ? "Rebuild online: add WITH (ONLINE = ON); or use ALTER INDEX ... REORGANIZE, which is always online."
            : $"ONLINE rebuild is not available on {TargetParser.EditionToken(config.Edition)} edition; use ALTER INDEX ... REORGANIZE, which is always online.";
}
