using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

/// <summary>
/// Auto-discovering .sql fixture harness. Every file under <c>Fixtures/&lt;RULE-ID&gt;/</c> runs
/// through the full <see cref="MsSqlAnalyzer"/> (reflection-discovered rules) and is checked
/// against the directives in its comments:
/// <code>
/// -- planizer-test: version=2019 edition=Standard rollback=true   (optional; defaults otherwise)
/// -- expect: MSSQL-RW-002 severity=Critical line=4  (severity/line optional)
/// -- expect-none: MSSQL-RW-001                      (this rule must NOT fire)
/// </code>
/// A fixture without a single directive fails; <c>trigger*.sql</c> needs at least one
/// <c>expect</c>, <c>clean*.sql</c> at least one <c>expect-none</c>.
/// </summary>
public class FixtureTests
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static TheoryData<string> FixtureFiles()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory
                     .EnumerateFiles(FixturesRoot, "*.sql", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(FixturesRoot, path).Replace('\\', '/'));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void Fixture_expectations_hold(string relativePath)
    {
        var fullPath = Path.Combine(FixturesRoot, relativePath);
        var sql = File.ReadAllText(fullPath);
        var fixture = FixtureDirectives.Parse(sql, relativePath);

        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        if (fileName.StartsWith("trigger", StringComparison.OrdinalIgnoreCase))
        {
            Assert.True(fixture.Expects.Count > 0,
                $"{relativePath}: a trigger fixture needs at least one '-- expect:' directive.");
        }

        if (fileName.StartsWith("clean", StringComparison.OrdinalIgnoreCase))
        {
            Assert.True(fixture.ExpectNones.Count > 0,
                $"{relativePath}: a clean fixture needs at least one '-- expect-none:' directive.");
        }

        var report = new MsSqlAnalyzer().Analyze([(relativePath, sql)], fixture.Config);

        // A fixture must parse unless it explicitly expects a parse finding.
        if (fixture.Expects.All(e => e.RuleId != MsSqlAnalyzer.ParseRuleId))
        {
            var parseErrors = report.Findings.Where(f => f.RuleId == MsSqlAnalyzer.ParseRuleId).ToList();
            Assert.True(parseErrors.Count == 0,
                $"{relativePath}: fixture does not parse: " +
                string.Join("; ", parseErrors.Select(f => $"line {f.Location.Line}: {f.Message}")));
        }

        foreach (var expect in fixture.Expects)
        {
            var matched = report.Findings.Any(f =>
                f.RuleId == expect.RuleId
                && (expect.Severity is null || f.Severity == expect.Severity)
                && (expect.Line is null || f.Location.Line == expect.Line));

            Assert.True(matched,
                $"{relativePath}: no finding matches '{expect}'. Actual findings: {Describe(report)}");
        }

        foreach (var ruleId in fixture.ExpectNones)
        {
            var offending = report.Findings.Where(f => f.RuleId == ruleId).ToList();
            Assert.True(offending.Count == 0,
                $"{relativePath}: rule {ruleId} must not fire but produced: {Describe(report, ruleId)}");
        }
    }

    private static string Describe(Report report, string? onlyRuleId = null)
    {
        var findings = report.Findings
            .Where(f => onlyRuleId is null || f.RuleId == onlyRuleId)
            .Select(f => $"{f.RuleId} severity={f.Severity} line={f.Location.Line} \"{f.Message}\"")
            .ToList();
        return findings.Count == 0 ? "(none)" : string.Join(" | ", findings);
    }

    private sealed record Expectation(string RuleId, Severity? Severity, int? Line)
    {
        public override string ToString()
            => $"expect: {RuleId}"
               + (Severity is null ? "" : $" severity={Severity}")
               + (Line is null ? "" : $" line={Line}");
    }

    private sealed class FixtureDirectives
    {
        public required PlanizerConfig Config { get; init; }
        public required IReadOnlyList<Expectation> Expects { get; init; }
        public required IReadOnlyList<string> ExpectNones { get; init; }

        public static FixtureDirectives Parse(string sql, string relativePath)
        {
            var config = new PlanizerConfig();
            var expects = new List<Expectation>();
            var expectNones = new List<string>();
            var directiveCount = 0;

            foreach (var rawLine in sql.Split('\n'))
            {
                var line = rawLine.Trim();

                if (TryStrip(line, "-- planizer-test:", out var testArgs))
                {
                    directiveCount++;
                    config = ApplyTestArgs(config, testArgs, relativePath);
                }
                else if (TryStrip(line, "-- expect-none:", out var noneArgs))
                {
                    directiveCount++;
                    var tokens = Tokenize(noneArgs);
                    Assert.True(tokens.Length == 1,
                        $"{relativePath}: 'expect-none' takes exactly one rule id, got '{noneArgs}'.");
                    expectNones.Add(tokens[0]);
                }
                else if (TryStrip(line, "-- expect:", out var expectArgs))
                {
                    directiveCount++;
                    expects.Add(ParseExpect(expectArgs, relativePath));
                }
            }

            Assert.True(directiveCount > 0,
                $"{relativePath}: fixture has no planizer-test/expect/expect-none directives; empty fixtures are banned.");

            return new FixtureDirectives { Config = config, Expects = expects, ExpectNones = expectNones };
        }

        private static Expectation ParseExpect(string args, string relativePath)
        {
            var tokens = Tokenize(args);
            Assert.True(tokens.Length >= 1, $"{relativePath}: 'expect' needs a rule id.");

            var ruleId = tokens[0];
            Severity? severity = null;
            int? line = null;

            foreach (var token in tokens.Skip(1))
            {
                if (TryStrip(token, "severity=", out var severityValue))
                {
                    severity = Enum.Parse<Severity>(severityValue, ignoreCase: true);
                }
                else if (TryStrip(token, "line=", out var lineValue))
                {
                    line = int.Parse(lineValue, System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    Assert.Fail($"{relativePath}: unknown 'expect' option '{token}'.");
                }
            }

            return new Expectation(ruleId, severity, line);
        }

        private static PlanizerConfig ApplyTestArgs(PlanizerConfig config, string args, string relativePath)
        {
            foreach (var token in Tokenize(args))
            {
                if (TryStrip(token, "version=", out var version))
                {
                    config = config with { TargetVersion = TargetParser.ParseVersion(version) };
                }
                else if (TryStrip(token, "edition=", out var edition))
                {
                    config = config with { Edition = TargetParser.ParseEdition(edition) };
                }
                else if (TryStrip(token, "rollback=", out var rollback))
                {
                    config = config with { Rollback = bool.Parse(rollback) };
                }
                else
                {
                    Assert.Fail($"{relativePath}: unknown 'planizer-test' option '{token}'.");
                }
            }

            return config;
        }

        private static string[] Tokenize(string args)
            => args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        private static bool TryStrip(string text, string prefix, out string rest)
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
            {
                rest = text[prefix.Length..].Trim();
                return true;
            }

            rest = string.Empty;
            return false;
        }
    }
}
