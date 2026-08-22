using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Locking;

/// <summary>
/// MSSQL-LOCK-005: a long online index operation without RESUMABLE = ON loses all progress on
/// failure or manual abort. Resumable is available for ALTER INDEX REBUILD since SQL Server 2017
/// and for CREATE INDEX since SQL Server 2019 (online operations only). Silent on
/// Standard/Express, where ONLINE = ON itself cannot run (MSSQL-LOCK-003 blocks it).
/// </summary>
public sealed class ResumableIndexRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-LOCK-005";
    public override string Title => "Online index operation without RESUMABLE";
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
                    when context.Config.TargetVersion >= SqlServerVersion.Sql2019
                    => ((IndexStatement)create, "CREATE INDEX"),
                AlterIndexStatement { AlterIndexType: AlterIndexType.Rebuild } rebuild
                    when context.Config.TargetVersion >= SqlServerVersion.Sql2017
                    => ((IndexStatement)rebuild, "ALTER INDEX REBUILD"),
                _ => (null, ""),
            };

            if (index is null
                || !IndexOptionInspector.IsOnline(index.IndexOptions)
                || IndexOptionInspector.IsResumable(index.IndexOptions))
            {
                continue;
            }

            var table = IndexOptionInspector.TargetTable(index);
            yield return CreateFinding(statement, DefaultSeverity,
                $"Online {operation} on {table} is not resumable; a failure or abort loses all progress and any long rollback blocks the table.",
                "Add RESUMABLE = ON, MAX_DURATION = 60 so the operation can be paused and resumed.");
        }
    }
}
