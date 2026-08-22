using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Locking;

/// <summary>
/// MSSQL-LOCK-010: DDL inside an explicit transaction with no earlier <c>SET LOCK_TIMEOUT</c> in
/// the same script. The default lock timeout is infinite, so a contended Sch-M lock makes the
/// migration wait forever while everything queues behind it. One finding per transaction,
/// anchored to its first DDL statement.
/// </summary>
public sealed class MissingLockTimeoutRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-LOCK-010";
    public override string Title => "Transactional DDL without SET LOCK_TIMEOUT";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        // First SET LOCK_TIMEOUT per file, computed once: the per-transaction check is then O(1)
        // instead of a scan over every statement.
        var firstTimeoutByFile = context.Statements
            .Where(s => IsSetLockTimeout(s.Ast))
            .GroupBy(s => s.Location.File, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Min(s => s.Index), StringComparer.Ordinal);

        foreach (var scope in context.Transactions)
        {
            var firstDdl = scope.StatementIndices
                .Select(context.StatementAt)
                .FirstOrDefault(s => s.Kind == StatementKind.Ddl);

            if (firstDdl is null || HasLockTimeoutBefore(firstTimeoutByFile, firstDdl))
            {
                continue;
            }

            yield return CreateFinding(firstDdl, DefaultSeverity,
                "DDL inside an explicit transaction waits indefinitely for its locks; no SET LOCK_TIMEOUT appears earlier in the script.",
                "Add SET LOCK_TIMEOUT 30000; at the top of the script so a blocked migration fails fast instead of queueing everything behind it.");
        }
    }

    private static bool HasLockTimeoutBefore(IReadOnlyDictionary<string, int> firstTimeoutByFile, SqlStatementInfo ddl)
        => firstTimeoutByFile.TryGetValue(ddl.Location.File, out var first) && first < ddl.Index;

    private static bool IsSetLockTimeout(TSqlStatement ast)
        => ast is SetCommandStatement set
            && set.Commands.OfType<GeneralSetCommand>()
                .Any(c => c.CommandType == GeneralSetCommandType.LockTimeout);
}
