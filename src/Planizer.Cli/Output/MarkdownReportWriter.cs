using System.Text;
using Planizer.Core;

namespace Planizer.Cli.Output;

/// <summary>
/// Markdown report meant to be pasted into a PR comment: header with files and assumption,
/// findings as a severity-ordered table, the rollback script in a collapsed block, and a
/// one-line summary at the bottom.
/// </summary>
public sealed class MarkdownReportWriter : IReportWriter
{
    private readonly bool _showTiming;

    /// <param name="showTiming">Append a collapsed timing table (<c>--timing</c>).</param>
    public MarkdownReportWriter(bool showTiming = false)
    {
        _showTiming = showTiming;
    }

    public void Write(Report report, TextWriter output)
    {
        output.WriteLine("# Planizer report");
        output.WriteLine();
        output.WriteLine($"**Files:** {string.Join(", ", report.Files.Select(f => $"`{f}`"))}");
        output.WriteLine($"**Assumption:** SQL Server {report.TargetVersion}, {report.Edition} edition, {ModeText(report.Mode)}");
        output.WriteLine();

        WriteFindings(report, output);
        WriteRollback(report.Summary, output);
        WriteSummaryLine(report, output);

        if (_showTiming && report.Timing is { } timing)
        {
            WriteTiming(timing, output);
        }
    }

    private static void WriteTiming(AnalysisTiming timing, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine("<details>");
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        output.WriteLine(string.Create(inv,
            $"<summary>Timing \u2014 parse {timing.ParseMs:F0} ms, rules {timing.RulesMs:F0} ms, total {timing.TotalMs:F0} ms</summary>"));
        output.WriteLine();
        output.WriteLine("| Rule | ms | Findings |");
        output.WriteLine("| --- | ---: | ---: |");
        foreach (var rule in timing.Slowest(10))
        {
            output.WriteLine(string.Create(inv, $"| {rule.RuleId} | {rule.ElapsedMs:F1} | {rule.FindingCount} |"));
        }

        output.WriteLine();
        output.WriteLine("</details>");
    }

    private static void WriteFindings(Report report, TextWriter output)
    {
        if (report.Findings.Count == 0)
        {
            output.WriteLine("No findings.");
            output.WriteLine();
            return;
        }

        output.WriteLine("| Severity | Rule | Location | Message | Fix |");
        output.WriteLine("| --- | --- | --- | --- | --- |");

        var ordered = report.Findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Location.File, StringComparer.Ordinal)
            .ThenBy(f => f.Location.Line)
            .ThenBy(f => f.Location.Column)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal);

        foreach (var finding in ordered)
        {
            var location = $"`{finding.Location.File}:{finding.Location.Line}:{finding.Location.Column}`";
            if (!string.IsNullOrWhiteSpace(finding.StatementSummary))
            {
                location += $"<br>`{Escape(finding.StatementSummary.Replace("`", "'"))}`";
            }

            var message = new StringBuilder(Escape(finding.Message));
            if (finding.Suppressed)
            {
                message.Append(string.IsNullOrWhiteSpace(finding.SuppressReason)
                    ? " _[suppressed]_"
                    : $" _[suppressed: {Escape(finding.SuppressReason)}]_");
            }

            if (finding.Inconclusive)
            {
                message.Append(" _[inconclusive]_");
            }

            var fix = finding.Fix is null ? "—" : Escape(finding.Fix);
            output.WriteLine(
                $"| {finding.Severity} | {finding.RuleId} | {location} | {message} | {fix} |");
        }

        output.WriteLine();
    }

    private static void WriteRollback(ScriptSummary summary, TextWriter output)
    {
        if (summary.RollbackComplete is not { } rollbackComplete || summary.RollbackScript.Count == 0)
        {
            return;
        }

        var status = rollbackComplete
            ? "complete — reverses every state-changing statement"
            : "incomplete — some statements need a manual rollback";

        output.WriteLine("<details>");
        output.WriteLine($"<summary>Rollback script ({status})</summary>");
        output.WriteLine();
        output.WriteLine("```sql");
        foreach (var statement in summary.RollbackScript)
        {
            output.WriteLine(statement);
        }

        output.WriteLine("```");
        output.WriteLine();
        output.WriteLine("</details>");
        output.WriteLine();
    }

    private static void WriteSummaryLine(Report report, TextWriter output)
    {
        var summary = report.Summary;
        var severityCounts = string.Join(", ",
            new[] { Severity.Blocker, Severity.Critical, Severity.Warning, Severity.Info }
                .Select(severity => $"{report.Findings.Count(f => f.Severity == severity)} {severity}"));

        output.WriteLine(
            $"**Summary:** {summary.StatementCount} statements ({summary.DdlCount} DDL, " +
            $"{summary.SchMLockCount} Sch-M, {summary.IrreversibleCount} irreversible, " +
            $"{summary.UnanalyzableCount} unanalyzable) · findings: {severityCounts} " +
            $"({report.SuppressedCount} suppressed)" +
            (summary.RollbackComplete is { } rollbackComplete
                ? $" · rollback {(rollbackComplete ? "complete" : "incomplete")}"
                : string.Empty));
    }

    /// <summary>Keeps arbitrary text inside a single markdown table cell.</summary>
    private static string Escape(string text)
        => text.Replace("|", "\\|").ReplaceLineEndings("<br>");

    private static string ModeText(AnalysisMode mode) => mode switch
    {
        AnalysisMode.Offline => "offline mode",
        AnalysisMode.Snapshot => "snapshot mode",
        AnalysisMode.Live => "live mode",
        _ => mode.ToString(),
    };
}
