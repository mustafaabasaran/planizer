using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Reversibility;

/// <summary>
/// MSSQL-REV-005: <c>SET IDENTITY_INSERT … ON</c> with no matching <c>OFF</c> for the same table
/// later in the same file. Only one table per session can have IDENTITY_INSERT ON; leaving it on
/// breaks every later identity insert on that connection until the session ends.
/// </summary>
public sealed class IdentityInsertLeftOnRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-REV-005";
    public override string Title => "SET IDENTITY_INSERT ON is never turned OFF";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        var switches = context.Statements
            .Where(s => s.Ast is SetIdentityInsertStatement)
            .Select(s => (Statement: s, Ast: (SetIdentityInsertStatement)s.Ast))
            .ToList();

        foreach (var (statement, ast) in switches)
        {
            if (!ast.IsOn)
            {
                continue;
            }

            var table = NormalizedName(ast.Table);
            var hasMatchingOff = switches.Any(other =>
                !other.Ast.IsOn
                && other.Statement.Index > statement.Index
                && other.Statement.Location.File == statement.Location.File
                && NormalizedName(other.Ast.Table) == table);

            if (hasMatchingOff)
            {
                continue;
            }

            var display = Render(ast.Table);
            yield return CreateFinding(statement, Severity.Warning,
                $"SET IDENTITY_INSERT {display} ON has no matching OFF in this script; only one " +
                "table per session can have it ON, and it stays ON until the session ends.",
                fix: $"SET IDENTITY_INSERT {display} OFF;");
        }
    }

    /// <summary>Case- and quoting-insensitive comparison key ([dbo].[T] equals dbo.T).</summary>
    private static string NormalizedName(SchemaObjectName? name)
        => name is null
            ? ""
            : string.Join(".", name.Identifiers.Select(i => i.Value.ToLowerInvariant()));

    private static string Render(SchemaObjectName? name)
        => name is null || name.Identifiers.Count == 0
            ? "the table"
            : string.Join(".", name.Identifiers.Select(i => i.Value));
}
