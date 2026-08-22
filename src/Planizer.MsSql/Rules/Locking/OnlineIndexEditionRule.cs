using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Locking;

/// <summary>
/// MSSQL-LOCK-003: ONLINE = ON on an index operation while the target edition is Standard or
/// Express — online index build/rebuild is an Enterprise (and Azure) feature, so the statement
/// does not run at all (error 1712).
/// </summary>
public sealed class OnlineIndexEditionRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-LOCK-003";
    public override string Title => "ONLINE = ON is not available on this edition";
    public override Severity DefaultSeverity => Severity.Blocker;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        if (context.Config.Edition is not (SqlEdition.Standard or SqlEdition.Express))
        {
            yield break;
        }

        var edition = TargetParser.EditionToken(context.Config.Edition);

        foreach (var statement in context.Statements)
        {
            if (statement.Ast is IndexStatement index
                && IndexOptionInspector.IsOnline(index.IndexOptions))
            {
                yield return CreateFinding(statement, DefaultSeverity,
                    $"ONLINE = ON requires Enterprise edition (or Azure); on {edition} this statement fails with error 1712.",
                    "Remove ONLINE = ON, or correct the edition assumption (--edition) if the target server is Enterprise.");
            }
        }
    }
}
