using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Hygiene;

/// <summary>
/// MSSQL-ENV-001: <c>USE [db]</c> switches the session to a hard-coded database. Migration
/// runners (EF Core, Flyway, DbUp, sqlcmd -d) already connect to the target database; a USE
/// overrides that choice and, where the name differs per environment, either fails or silently
/// runs the script somewhere else.
/// </summary>
public sealed class EnvUseDatabaseRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-ENV-001";
    public override string Title => "USE [database] overrides the migration runner's target database";
    public override Severity DefaultSeverity => Severity.Info;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (statement.Ast is not UseStatement use)
            {
                continue;
            }

            var database = use.DatabaseName?.Value ?? "the database";
            yield return CreateFinding(statement, DefaultSeverity,
                $"USE {database} pins the script to a database name: the migration runner already " +
                "connects to the target database, and on an environment where the name differs the " +
                "script fails or runs against the wrong database.",
                "Remove the USE statement and let the runner's connection choose the database.");
        }
    }
}
