using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Hygiene;

/// <summary>
/// MSSQL-SET-002: a file with many data-modification statements (INSERT / UPDATE / DELETE /
/// MERGE / BULK INSERT — plain SELECTs do not count) and no <c>SET NOCOUNT ON</c> sends a
/// "(n rows affected)" DONE message back to the client for every statement — extra bytes in
/// the same TDS response (not extra round trips) and a flooded runner log. One Info per file
/// (ADR-0001 pattern), anchored at the first write; only files with at least
/// <see cref="Threshold"/> writes are reported.
/// </summary>
public sealed class SetNoCountRule : MsSqlRuleBase
{
    /// <summary>Minimum number of data-modification statements in a file before the missing SET NOCOUNT ON is worth a finding.</summary>
    public const int Threshold = 50;

    public override string Id => "MSSQL-SET-002";
    public override string Title => "Many DML statements without SET NOCOUNT ON";
    public override Severity DefaultSeverity => Severity.Info;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var file in context.Statements.Select(s => s.Location.File).Distinct(StringComparer.Ordinal))
        {
            var statements = context.StatementsInFile(file).ToList();

            if (statements.Any(IsNoCountOn))
            {
                continue;
            }

            var dml = statements
                .Where(s => IsWrite(s.Ast) && !s.SuppressedRuleIds.Contains(Id))
                .ToList();

            if (dml.Count < Threshold)
            {
                continue;
            }

            yield return CreateFinding(dml[0], DefaultSeverity,
                $"{dml.Count} data-modification statements in this file run without SET NOCOUNT ON: " +
                "each one returns a \"rows affected\" message to the client, which adds a DONE message per " +
                "statement to the response and a line to the migration runner's log.",
                "Add at the top of the script:\nSET NOCOUNT ON;");
        }
    }

    /// <summary>Statements that produce a "rows affected" message: writes, whatever their target.</summary>
    private static bool IsWrite(TSqlStatement ast)
        => ast is InsertStatement or UpdateStatement or DeleteStatement or MergeStatement or BulkInsertStatement;

    private static bool IsNoCountOn(SqlStatementInfo statement)
        => statement.Ast is PredicateSetStatement { IsOn: true } set
            && set.Options.HasFlag(SetOptions.NoCount);
}
