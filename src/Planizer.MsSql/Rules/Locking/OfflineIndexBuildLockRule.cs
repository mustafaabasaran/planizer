using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Locking;

/// <summary>
/// MSSQL-LOCK-002: CREATE INDEX without ONLINE = ON builds offline — a nonclustered build holds a
/// shared (S) table lock (writes blocked), a clustered build holds Sch-M (all access blocked).
/// The fix depends on the edition: Enterprise/Azure can go online, Standard/Express cannot.
/// </summary>
public sealed class OfflineIndexBuildLockRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-LOCK-002";
    public override string Title => "Offline index build blocks access to the table";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (statement.Ast is not CreateIndexStatement create
                || IndexOptionInspector.IsOnline(create.IndexOptions))
            {
                continue;
            }

            var table = IndexOptionInspector.TargetTable(create);
            var message = create.Clustered == true
                ? $"Offline clustered index build takes a schema-modification (Sch-M) lock on {table}: all access blocked until the build completes."
                : $"Offline nonclustered index build takes a shared (S) lock on {table}: writes blocked during build.";

            yield return CreateFinding(statement, DefaultSeverity, message, Fix(context.Config));
        }
    }

    private static string Fix(PlanizerConfig config)
        => config.Edition is SqlEdition.Enterprise or SqlEdition.Azure
            ? "Build the index online: add WITH (ONLINE = ON)."
            : $"ONLINE index build is not available on {TargetParser.EditionToken(config.Edition)} edition; schedule a maintenance window.";
}
