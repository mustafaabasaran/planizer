using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Locking;

/// <summary>
/// MSSQL-LOCK-004: an online index operation still takes a brief Sch-M lock at the start and end.
/// Without WAIT_AT_LOW_PRIORITY that brief lock queues at normal priority and can convoy every
/// session behind a single long-running transaction. Gated by what actually accepts the syntax:
/// ALTER INDEX … REBUILD since SQL Server 2014, CREATE INDEX only since SQL Server 2022 —
/// suggesting it to an older target would recommend syntax the server rejects. Silent on
/// Standard/Express, where ONLINE = ON itself cannot run (MSSQL-LOCK-003 blocks it).
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
            var (index, operation) = statement.Ast switch
            {
                CreateIndexStatement create
                    when context.Config.TargetVersion >= SqlServerVersion.Sql2022
                    => ((IndexStatement)create, "CREATE INDEX"),
                AlterIndexStatement { AlterIndexType: AlterIndexType.Rebuild } rebuild
                    when context.Config.TargetVersion >= SqlServerVersion.Sql2014
                    => ((IndexStatement)rebuild, "ALTER INDEX REBUILD"),
                _ => (null, ""),
            };

            if (index is null
                || !IndexOptionInspector.IsOnline(index.IndexOptions)
                || IndexOptionInspector.HasWaitAtLowPriority(index.IndexOptions))
            {
                continue;
            }

            var table = IndexOptionInspector.TargetTable(index);
            yield return CreateFinding(statement, DefaultSeverity,
                $"Online {operation} still takes brief Sch-M locks on {table} at the start and end; without WAIT_AT_LOW_PRIORITY they queue at normal priority and can convoy blocked sessions.",
                "Use WITH (ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)))");
        }
    }
}
