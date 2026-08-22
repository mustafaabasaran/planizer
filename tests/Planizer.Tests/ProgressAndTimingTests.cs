using System.Text.Json;
using Planizer.Cli.Output;
using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

/// <summary>
/// Progress reporting (<see cref="IProgress{T}"/> hook on the analyzer, stderr renderer in the CLI)
/// and per-rule timing in the report.
/// </summary>
public class ProgressAndTimingTests
{
    private const string Sql = "ALTER TABLE dbo.T ADD C int NOT NULL DEFAULT 0;\nDROP TABLE dbo.Old;";

    private sealed class Collector : IProgress<AnalysisProgress>
    {
        public List<AnalysisProgress> Ticks { get; } = [];
        public void Report(AnalysisProgress value) => Ticks.Add(value);
    }

    [Fact]
    public void Analyzer_reports_parsing_per_file_then_rules_then_finishing_in_order()
    {
        var collector = new Collector();
        var ruleCount = MsSqlAnalyzer.DiscoverRules().Count;

        new MsSqlAnalyzer().Analyze([("a.sql", Sql), ("b.sql", "SELECT 1;")], new PlanizerConfig(), collector);

        var parsing = collector.Ticks.Where(t => t.Phase == AnalysisPhase.Parsing).ToList();
        var rules = collector.Ticks.Where(t => t.Phase == AnalysisPhase.Rules).ToList();

        Assert.Equal(["a.sql", "b.sql"], parsing.Select(t => t.Label));
        Assert.Equal([1, 2], parsing.Select(t => t.Current));
        Assert.All(parsing, t => Assert.Equal(2, t.Total));

        Assert.Equal(ruleCount, rules.Count);
        Assert.Equal(Enumerable.Range(1, ruleCount), rules.Select(t => t.Current));
        Assert.All(rules, t => Assert.Equal(ruleCount, t.Total));
        Assert.Contains(rules, t => t.Label == "MSSQL-LOCK-001");

        Assert.Equal(AnalysisPhase.Finishing, collector.Ticks[^1].Phase);
        Assert.True(collector.Ticks.Select(t => t.Phase).SequenceEqual(collector.Ticks.Select(t => t.Phase).OrderBy(p => p)),
            "phases must not interleave");
    }

    [Fact]
    public void Disabled_rule_is_neither_run_nor_counted_in_progress_or_timing()
    {
        var collector = new Collector();
        var config = new PlanizerConfig
        {
            Rules = new Dictionary<string, RuleOverride> { ["MSSQL-LOCK-001"] = new(Enabled: false) },
        };

        var report = new MsSqlAnalyzer().Analyze([("a.sql", Sql)], config, collector);

        var total = MsSqlAnalyzer.DiscoverRules().Count - 1;
        Assert.All(collector.Ticks.Where(t => t.Phase == AnalysisPhase.Rules), t => Assert.Equal(total, t.Total));
        Assert.DoesNotContain(collector.Ticks, t => t.Label == "MSSQL-LOCK-001");
        Assert.DoesNotContain(report.Timing!.Rules, r => r.RuleId == "MSSQL-LOCK-001");
    }

    [Fact]
    public void Report_carries_one_timing_entry_per_rule_with_its_finding_count()
    {
        var report = new MsSqlAnalyzer().Analyze([("a.sql", Sql)], new PlanizerConfig());

        var timing = Assert.IsType<AnalysisTiming>(report.Timing);
        Assert.Equal(MsSqlAnalyzer.DiscoverRules().Count, timing.Rules.Count);
        Assert.All(timing.Rules, r => Assert.True(r.ElapsedMs >= 0));
        Assert.True(timing.TotalMs >= timing.RulesMs);
        Assert.Equal(report.Findings.Count(f => f.RuleId == "MSSQL-LOCK-001"),
            timing.Rules.Single(r => r.RuleId == "MSSQL-LOCK-001").FindingCount);
        Assert.Equal(timing.Rules.OrderByDescending(r => r.ElapsedMs).First().RuleId, timing.Slowest(1).Single().RuleId);
    }

    [Fact]
    public void Renderer_draws_in_place_on_stderr_and_erases_the_line_on_dispose()
    {
        var error = new StringWriter();
        using (var renderer = new ProgressRenderer(error, TimeSpan.Zero, width: 80))
        {
            renderer.Report(new AnalysisProgress(AnalysisPhase.Parsing, 1, 2, "a.sql"));
            renderer.Report(new AnalysisProgress(AnalysisPhase.Parsing, 2, 2, "b.sql"));
            renderer.Report(new AnalysisProgress(AnalysisPhase.Rules, 1, 3, "MSSQL-LOCK-001"));
            renderer.Report(new AnalysisProgress(AnalysisPhase.Finishing, 1, 1, "summary"));
        }

        var text = error.ToString();
        Assert.Contains("parsing 1/2  a.sql", text);
        Assert.Contains("parsing 2/2  b.sql", text);
        Assert.Contains("rules 1/3  MSSQL-LOCK-001", text);
        Assert.Contains("finishing", text);
        Assert.DoesNotContain('\n', text);
        Assert.EndsWith("\r\x1b[2K", text);
    }

    [Fact]
    public void Renderer_throttles_intermediate_ticks_but_always_shows_phase_edges()
    {
        var error = new StringWriter();
        using (var renderer = new ProgressRenderer(error, TimeSpan.FromHours(1), width: 80))
        {
            for (var i = 1; i <= 50; i++)
            {
                renderer.Report(new AnalysisProgress(AnalysisPhase.Rules, i, 50, $"RULE-{i:000}"));
            }
        }

        var text = error.ToString();
        Assert.Contains("rules 1/50", text);
        Assert.Contains("rules 50/50", text);
        Assert.DoesNotContain("rules 25/50", text);
    }

    [Fact]
    public void Renderer_truncates_long_lines_to_the_terminal_width()
    {
        var error = new StringWriter();
        using var renderer = new ProgressRenderer(error, TimeSpan.Zero, width: 40);

        renderer.Report(new AnalysisProgress(AnalysisPhase.Parsing, 1, 1, new string('x', 200)));

        var line = error.ToString().Replace("\r\x1b[2K", "");
        Assert.True(line.Length < 40, $"line was {line.Length} chars");
        Assert.EndsWith("…", line);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void Renderer_is_shown_only_on_an_interactive_stderr(bool noProgress, bool redirected, bool expected)
        => Assert.Equal(expected, ProgressRenderer.ShouldShow(noProgress, redirected));

    [Fact]
    public void Text_writer_prints_timing_block_only_when_asked()
    {
        var report = new MsSqlAnalyzer().Analyze([("a.sql", Sql)], new PlanizerConfig());

        var with = Write(new TextReportWriter(showTiming: true), report);
        var without = Write(new TextReportWriter(), report);

        Assert.Contains("Timing", with);
        Assert.Contains("Slowest rules:", with);
        Assert.Contains(report.Timing!.Slowest(1).Single().RuleId, with);
        Assert.DoesNotContain("Timing", without);
    }

    [Fact]
    public void Markdown_writer_prints_collapsed_timing_table_only_when_asked()
    {
        var report = new MsSqlAnalyzer().Analyze([("a.sql", Sql)], new PlanizerConfig());

        var with = Write(new MarkdownReportWriter(showTiming: true), report);
        var without = Write(new MarkdownReportWriter(), report);

        Assert.Contains("<summary>Timing", with);
        Assert.Contains("| Rule | ms | Findings |", with);
        Assert.DoesNotContain("Timing", without);
    }

    [Fact]
    public void Json_output_always_carries_timing()
    {
        var report = new MsSqlAnalyzer().Analyze([("a.sql", Sql)], new PlanizerConfig());

        using var json = JsonDocument.Parse(Write(new JsonReportWriter(), report));
        var timing = json.RootElement.GetProperty("timing");

        Assert.True(timing.GetProperty("totalMs").GetDouble() >= 0);
        Assert.Equal(report.Timing!.Rules.Count, timing.GetProperty("rules").GetArrayLength());
    }

    [Fact]
    public void Timing_numbers_use_invariant_culture()
    {
        var report = new MsSqlAnalyzer().Analyze([("a.sql", Sql)], new PlanizerConfig());
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            var text = Write(new TextReportWriter(showTiming: true), report);
            var markdown = Write(new MarkdownReportWriter(showTiming: true), report);

            Assert.DoesNotMatch(@"\d,\d ms", text);
            Assert.DoesNotMatch(@"\| \d+,\d \|", markdown);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    private static string Write(IReportWriter writer, Report report)
    {
        var output = new StringWriter();
        writer.Write(report, output);
        return output.ToString();
    }
}
