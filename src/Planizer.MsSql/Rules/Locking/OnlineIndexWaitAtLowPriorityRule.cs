using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Locking;

/// <summary>
/// MSSQL-LOCK-004: an online index operation is not lock-free — it still needs a brief table lock
/// to start and another to complete, and without WAIT_AT_LOW_PRIORITY those brief locks queue at
/// normal priority and can convoy every session behind a single long-running transaction.
///
/// Which locks depends on the operation. Per Microsoft's online index phase table, the preparation
/// phase always takes a shared (S) lock on the table; the final phase takes S again for a
/// nonclustered CREATE, and a schema-modification (Sch-M) lock when a clustered index is created
/// or dropped online or when any index is rebuilt. (The Sch-M that an online build holds for its
/// whole duration is an object lock of resource subtype INDEX_OPERATION: it blocks concurrent DDL,
/// not DML, so it is not what makes the operation "block access".)
///
/// Gated by what actually accepts the syntax: ALTER INDEX … REBUILD since SQL Server 2014,
/// CREATE INDEX only since SQL Server 2022 — suggesting it to an older target would recommend
/// syntax the server rejects. Silent on Standard/Express, where ONLINE = ON itself cannot run
/// (MSSQL-LOCK-003 blocks it).
/// </summary>
public sealed class OnlineIndexWaitAtLowPriorityRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-LOCK-004";
    public override string Title => "Online index operation without WAIT_AT_LOW_PRIORITY";
    public override Severity DefaultSeverity => Severity.Info;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        if (context.Config.Edition is not (SqlEdition.Enterprise or SqlEdition.Azure))
        {
            // ONLINE = ON fails outright on this edition; tuning advice would contradict LOCK-003.
            yield break;
        }

        foreach (var statement in context.Statements)
        {
            // finalIsSchM: the final phase takes Sch-M for an online clustered create and for any
            // rebuild; a nonclustered create ends on a second shared (S) lock instead.
            var (index, operation, finalIsSchM) = statement.Ast switch
            {
                CreateIndexStatement create
                    when context.Config.TargetVersion >= SqlServerVersion.Sql2022
                    => ((IndexStatement)create, "CREATE INDEX", create.Clustered == true),
                AlterIndexStatement { AlterIndexType: AlterIndexType.Rebuild } rebuild
                    when context.Config.TargetVersion >= SqlServerVersion.Sql2014
                    => ((IndexStatement)rebuild, "ALTER INDEX REBUILD", true),
                _ => (null, "", false),
            };

            if (index is null
                || !IndexOptionInspector.IsOnline(index.IndexOptions)
                || IndexOptionInspector.HasWaitAtLowPriority(index.IndexOptions))
            {
                continue;
            }

            var table = IndexOptionInspector.TargetTable(index);
            var locks = finalIsSchM
                ? $"a brief shared (S) lock on {table} to start and a schema-modification (Sch-M) lock to complete"
                : $"a brief shared (S) lock on {table} to start and again to complete";

            yield return CreateFinding(statement, DefaultSeverity,
                $"Online {operation} still needs {locks}; without WAIT_AT_LOW_PRIORITY they queue at normal priority and can convoy blocked sessions.",
                "Use WITH (ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)))");
        }
    }
}
