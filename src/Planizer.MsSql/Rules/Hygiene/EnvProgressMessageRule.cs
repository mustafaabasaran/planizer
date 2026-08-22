using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Hygiene;

/// <summary>
/// MSSQL-ENV-003: the file contains DDL that, under the configured target, rewrites a table,
/// scans it in full or builds an index (catalog <c>data_movement</c> = rewrite / full_scan /
/// index_build) and never announces progress — no <c>PRINT</c> and no informational
/// <c>RAISERROR</c> (severity ≤ 10, or <c>WITH NOWAIT</c>). The run is silent until it finishes
/// or fails, and nobody can tell which step is taking the time. One Info per file (ADR-0001
/// pattern), anchored at the first long-running statement. Statements the catalog cannot
/// classify offline are not counted. A RAISERROR with severity ≥ 11 is error handling, not
/// progress.
/// </summary>
public sealed class EnvProgressMessageRule : MsSqlRuleBase
{
    private const int ExampleCount = 3;

    public override string Id => "MSSQL-ENV-003";
    public override string Title => "Long-running DDL with no progress messages";
    public override Severity DefaultSeverity => Severity.Info;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var file in context.Statements.Select(s => s.Location.File).Distinct(StringComparer.Ordinal))
        {
            var statements = context.StatementsInFile(file).ToList();

            if (statements.Any(s => IsProgressMessage(s.Ast)))
            {
                continue;
            }

            var longRunning = statements
                .Where(s => s.Kind == StatementKind.Ddl
                    && !s.SuppressedRuleIds.Contains(Id)
                    && DdlOperationClassifier.GetBehavior(s, context.Catalog, context.Config) is
                        { Movement: DataMovement.Rewrite or DataMovement.FullScan or DataMovement.IndexBuild })
                .ToList();

            if (longRunning.Count == 0)
            {
                continue;
            }

            var examples = longRunning.Take(ExampleCount).Select(s => $"`{DescribeStatement(s, 60)}`");

            yield return CreateFinding(longRunning[0], DefaultSeverity,
                $"{longRunning.Count} statement{(longRunning.Count == 1 ? "" : "s")} in this file " +
                $"rewrite{(longRunning.Count == 1 ? "s" : "")}, scan{(longRunning.Count == 1 ? "s" : "")} or " +
                $"build{(longRunning.Count == 1 ? "s" : "")} an index over a whole table " +
                $"({string.Join(", ", examples)}) and the script prints no progress message: a long run is " +
                "silent until it finishes or fails, with no way to tell which step is taking the time.",
                "Announce each step so the runner log shows where the script is, e.g. before each long statement:\n" +
                "RAISERROR('step: <description>', 0, 1) WITH NOWAIT;");
        }
    }

    private static bool IsProgressMessage(TSqlStatement statement) => statement switch
    {
        PrintStatement => true,
        RaiseErrorStatement raise => raise.RaiseErrorOptions.HasFlag(RaiseErrorOptions.NoWait)
                                     || IsInformationalSeverity(raise.SecondParameter),
        _ => false,
    };

    /// <summary>Severity 0–10 is a message, not an error; an unknown (non-literal) severity is not assumed to be one.</summary>
    private static bool IsInformationalSeverity(ScalarExpression? severity)
        => severity is IntegerLiteral literal
            && int.TryParse(literal.Value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
            && value <= 10;
}
