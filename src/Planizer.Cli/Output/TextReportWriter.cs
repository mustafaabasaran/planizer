using Planizer.Core;

namespace Planizer.Cli.Output;

/// <summary>Human-readable report: one block per analyzed file, then a summary block.</summary>
public sealed class TextReportWriter : IReportWriter
{
    private const string Reset = "\x1b[0m";
    private const string Bold = "\x1b[1m";
    private const string Dim = "\x1b[2m";

    private readonly bool _useColor;
    private readonly bool _showTiming;

    /// <param name="useColor">
    /// Wrap severity tokens, headers and tags in ANSI color codes. The default is plain text;
    /// the CLI decides via <see cref="ShouldUseColor"/>.
    /// </param>
    /// <param name="showTiming">Append the timing block (<c>--timing</c>) after the summary.</param>
    public TextReportWriter(bool useColor = false, bool showTiming = false)
    {
        _useColor = useColor;
        _showTiming = showTiming;
    }

    /// <summary>
    /// Color is used only on a real terminal and only when the <c>NO_COLOR</c> convention
    /// (any non-empty value disables color) does not object.
    /// </summary>
    public static bool ShouldUseColor(string? noColorEnvValue, bool outputRedirected)
        => !outputRedirected && string.IsNullOrEmpty(noColorEnvValue);

    public void Write(Report report, TextWriter output)
    {
        var findingsByFile = report.Findings.ToLookup(f => f.Location.File);

        foreach (var file in report.Files)
        {
            output.WriteLine(Colorize($"== {file} ==", Bold));

            var findings = findingsByFile[file]
                .OrderBy(f => f.Location.Line)
                .ThenBy(f => f.Location.Column)
                .ThenBy(f => f.RuleId, StringComparer.Ordinal)
                .ToList();

            if (findings.Count == 0)
            {
                output.WriteLine("  no findings");
            }

            // One header per statement (location + the statement itself), findings beneath it —
            // several rules firing on one statement, or the same rule on two similar statements,
            // stay distinguishable at a glance.
            foreach (var statement in findings.GroupBy(f => (f.Location.Line, f.Location.Column)))
            {
                var header = Colorize($"{file}:{statement.Key.Line}:{statement.Key.Column}", Bold);
                var summary = statement.Select(f => f.StatementSummary).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                output.WriteLine(summary is null ? $"  {header}" : $"  {header}  {Colorize(summary, Dim)}");

                foreach (var finding in statement)
                {
                    output.WriteLine($"    {FormatFinding(finding)}");
                    if (finding.Fix is not null)
                    {
                        output.WriteLine($"      fix: {finding.Fix}");
                    }
                }
            }

            output.WriteLine();
        }

        WriteSummary(report, output);

        if (_showTiming && report.Timing is { } timing)
        {
            WriteTiming(timing, output);
        }
    }

    private void WriteTiming(AnalysisTiming timing, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine(Colorize("Timing", Bold));
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        output.WriteLine(string.Create(inv,
            $"  Parse: {timing.ParseMs:F0} ms \u00b7 Rules: {timing.RulesMs:F0} ms \u00b7 Total: {timing.TotalMs:F0} ms ({timing.Rules.Count} rules)"));
        output.WriteLine("  Slowest rules:");
        foreach (var rule in timing.Slowest(5))
        {
            output.WriteLine(string.Create(inv,
                $"    {rule.RuleId,-16} {rule.ElapsedMs,9:F1} ms  ({rule.FindingCount} finding{(rule.FindingCount == 1 ? "" : "s")})"));
        }
    }

    private string FormatFinding(Finding finding)
    {
        var suppressedTag = string.IsNullOrWhiteSpace(finding.SuppressReason)
            ? " [suppressed]"
            : $" [suppressed: {finding.SuppressReason}]";
        var tags =
            (finding.Suppressed ? Colorize(suppressedTag, Dim) : string.Empty) +
            (finding.Inconclusive ? Colorize(" [inconclusive]", Dim) : string.Empty);

        return $"{Colorize(finding.Severity.ToString(), SeverityColor(finding.Severity))} {finding.RuleId} " +
               $"{finding.Message}{tags}";
    }

    private void WriteSummary(Report report, TextWriter output)
    {
        var summary = report.Summary;

        output.WriteLine(Colorize("Summary", Bold));
        output.WriteLine($"  Assumption: SQL Server {report.TargetVersion}, {report.Edition} edition, {ModeText(report.Mode)}");
        output.WriteLine(
            $"  Statements: {summary.StatementCount} total, {summary.DdlCount} DDL, " +
            $"{summary.SchMLockCount} taking Sch-M locks, {summary.IrreversibleCount} irreversible, " +
            $"{summary.UnanalyzableCount} unanalyzable");
        if (summary.RollbackComplete is { } rollbackComplete)
        {
            output.WriteLine(
                $"  Rollback:   {(rollbackComplete ? "complete" : "incomplete")} " +
                $"({summary.RollbackScript.Count} reverse statement(s) generated)");
        }
        output.WriteLine(
            $"  Findings:   {report.Findings.Count} total ({SeverityCounts(report)}), " +
            $"{report.SuppressedCount} suppressed");
    }

    private string Colorize(string text, string code)
        => _useColor ? $"{code}{text}{Reset}" : text;

    private static string SeverityColor(Severity severity) => severity switch
    {
        Severity.Blocker => "\x1b[91m",  // bright red
        Severity.Critical => "\x1b[31m", // red
        Severity.Warning => "\x1b[33m",  // yellow
        _ => "\x1b[36m",                 // cyan
    };

    private static string SeverityCounts(Report report)
        => string.Join(", ",
            new[] { Severity.Blocker, Severity.Critical, Severity.Warning, Severity.Info }
                .Select(severity => $"{report.Findings.Count(f => f.Severity == severity)} {severity}"));

    private static string ModeText(AnalysisMode mode) => mode switch
    {
        AnalysisMode.Offline => "offline mode",
        AnalysisMode.Snapshot => "snapshot mode",
        AnalysisMode.Live => "live mode",
        _ => mode.ToString(),
    };
}
