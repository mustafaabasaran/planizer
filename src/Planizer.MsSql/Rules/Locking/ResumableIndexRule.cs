using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Locking;

/// <summary>
/// MSSQL-LOCK-005: a long online index operation without RESUMABLE = ON loses all progress on
/// failure or manual abort. Resumable is available for ALTER INDEX REBUILD since SQL Server 2017
/// and for CREATE INDEX since SQL Server 2019 (online operations only). Silent on
/// Standard/Express, where ONLINE = ON itself cannot run (MSSQL-LOCK-003 blocks it).
/// The finding always stands, but the suggested fix depends on the transaction context:
/// "The DDL command with RESUMABLE = ON can't be executed inside an explicit transaction"
/// (Msg 574), so inside a BEGIN TRANSACTION … COMMIT block the advice is to move the statement
/// out rather than to add the option.
/// </summary>
public sealed class ResumableIndexRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-LOCK-005";
    public override string Title => "Online index operation without RESUMABLE";
    public override Severity DefaultSeverity => Severity.Info;

    /// <summary>
    /// A migration runner (DbUp with a transaction-per-script, SSDT, EF's transactional scripts)
    /// can open a transaction the script itself never mentions; RESUMABLE fails there too, and the
    /// script alone cannot show it. Appended to every suggestion so the advice is never wrong.
    /// </summary>
    private const string RunnerCaveat =
        " RESUMABLE = ON also fails when the migration runner wraps the script in its own "
        + "transaction — run the index operation outside any transaction.";

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
                Fix(operation, context.IsInExplicitTransaction(statement.Index)));
        }
    }

    private static string Fix(string operation, bool insideExplicitTransaction)
        => insideExplicitTransaction
            ? $"RESUMABLE = ON cannot be added here: this {operation} runs inside an explicit "
              + "transaction, and a resumable DDL command fails there with error 574. Move the "
              + $"{operation} out of the BEGIN TRANSACTION … COMMIT block, then add "
              + "RESUMABLE = ON, MAX_DURATION = 60." + RunnerCaveat
            : "Add RESUMABLE = ON, MAX_DURATION = 60 so the operation can be paused and resumed."
              + RunnerCaveat;
}
