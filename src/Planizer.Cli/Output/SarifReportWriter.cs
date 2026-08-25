using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Cli.Output;

/// <summary>Rule metadata published in the SARIF <c>tool.driver.rules</c> array.</summary>
public sealed record SarifRuleDescriptor(string Id, string Title, Severity DefaultSeverity);

/// <summary>
/// SARIF 2.1.0 report for GitHub code scanning and other SARIF consumers. Hand-written with
/// <see cref="Utf8JsonWriter"/> — no Sarif SDK dependency (ADR-0002). One run, one result per
/// finding, every rule listed in <c>tool.driver.rules</c> so <c>ruleIndex</c> always resolves.
/// File URIs are relative to the source root (the working directory in the CLI) with
/// <c>uriBaseId</c> <c>%SRCROOT%</c>; files outside the root fall back to absolute
/// <c>file://</c> URIs.
/// </summary>
public sealed class SarifReportWriter : IReportWriter
{
    public const string SchemaUri = "https://json.schemastore.org/sarif-2.1.0.json";
    public const string SarifVersion = "2.1.0";
    public const string ToolName = "Planizer";
    public const string InformationUri = "https://github.com/mustafaabasaran/planizer";
    public const string SourceRootId = "%SRCROOT%";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IReadOnlyList<SarifRuleDescriptor> _rules;
    private readonly string _sourceRoot;

    /// <param name="rules">Rules to publish; any finding whose rule is missing here is appended on the fly.</param>
    /// <param name="sourceRoot">Directory that <c>%SRCROOT%</c> stands for; relative file paths are resolved against it.</param>
    public SarifReportWriter(IReadOnlyList<SarifRuleDescriptor> rules, string sourceRoot)
    {
        _rules = rules;
        _sourceRoot = Path.GetFullPath(sourceRoot);
    }

    /// <summary>Writer pre-loaded with every MSSQL rule (including the analyzer-produced MSSQL-PARSE-001).</summary>
    public static SarifReportWriter ForMsSql(string sourceRoot) => new(MsSqlRules(), sourceRoot);

    public static IReadOnlyList<SarifRuleDescriptor> MsSqlRules()
        => MsSqlAnalyzer.DiscoverRules()
            .Select(rule => new SarifRuleDescriptor(rule.Id, rule.Title, rule.DefaultSeverity))
            .Append(new SarifRuleDescriptor(MsSqlAnalyzer.ParseRuleId, RulesCommand.ParseRuleTitle, Severity.Blocker))
            .OrderBy(rule => rule.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>SARIF <c>level</c> for a Planizer severity: Info→note, Warning→warning, Critical/Blocker→error.</summary>
    public static string ToLevel(Severity severity) => severity switch
    {
        Severity.Info => "note",
        Severity.Warning => "warning",
        _ => "error",
    };

    public void Write(Report report, TextWriter output)
    {
        var rules = ResolveRules(report);
        var ruleIndex = rules
            .Select((rule, index) => (rule.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("$schema", SchemaUri);
            json.WriteString("version", SarifVersion);

            json.WriteStartArray("runs");
            json.WriteStartObject();
            WriteTool(json, report, rules);
            WriteInvocations(json);
            WriteOriginalUriBaseIds(json);
            WriteResults(json, report, ruleIndex);
            json.WriteEndObject();
            json.WriteEndArray();

            json.WriteEndObject();
        }

        output.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    /// <summary>Configured rules plus a descriptor for any finding whose rule id is not configured.</summary>
    private IReadOnlyList<SarifRuleDescriptor> ResolveRules(Report report)
    {
        var known = new HashSet<string>(_rules.Select(r => r.Id), StringComparer.Ordinal);
        var extra = report.Findings
            .Where(f => known.Add(f.RuleId))
            .Select(f => new SarifRuleDescriptor(f.RuleId, f.RuleId, f.Severity));

        return _rules.Concat(extra).ToList();
    }

    private static void WriteTool(Utf8JsonWriter json, Report report, IReadOnlyList<SarifRuleDescriptor> rules)
    {
        json.WriteStartObject("tool");
        json.WriteStartObject("driver");
        json.WriteString("name", ToolName);
        json.WriteString("semanticVersion", report.ToolVersion);
        json.WriteString("informationUri", InformationUri);

        json.WriteStartArray("rules");
        foreach (var rule in rules)
        {
            json.WriteStartObject();
            json.WriteString("id", rule.Id);
            json.WriteString("name", rule.Title);

            json.WriteStartObject("shortDescription");
            json.WriteString("text", rule.Title);
            json.WriteEndObject();

            json.WriteStartObject("defaultConfiguration");
            json.WriteString("level", ToLevel(rule.DefaultSeverity));
            json.WriteEndObject();

            json.WriteStartObject("properties");
            json.WriteString("docs", $"docs/rules/{rule.Id}.md");
            json.WriteEndObject();

            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static void WriteInvocations(Utf8JsonWriter json)
    {
        json.WriteStartArray("invocations");
        json.WriteStartObject();
        json.WriteBoolean("executionSuccessful", true);
        json.WriteEndObject();
        json.WriteEndArray();
    }

    private void WriteOriginalUriBaseIds(Utf8JsonWriter json)
    {
        json.WriteStartObject("originalUriBaseIds");
        json.WriteStartObject(SourceRootId);
        json.WriteString("uri", DirectoryUri(_sourceRoot));
        json.WriteEndObject();
        json.WriteEndObject();
    }

    private void WriteResults(Utf8JsonWriter json, Report report, IReadOnlyDictionary<string, int> ruleIndex)
    {
        json.WriteStartArray("results");
        foreach (var finding in report.Findings)
        {
            json.WriteStartObject();
            json.WriteString("ruleId", finding.RuleId);
            json.WriteNumber("ruleIndex", ruleIndex[finding.RuleId]);
            json.WriteString("level", ToLevel(finding.Severity));

            json.WriteStartObject("message");
            json.WriteString("text", finding.Fix is null
                ? finding.Message
                : $"{finding.Message}\n\nFix: {finding.Fix}");
            json.WriteEndObject();

            WriteLocations(json, finding.Location);

            if (finding.Suppressed)
            {
                json.WriteStartArray("suppressions");
                json.WriteStartObject();
                json.WriteString("kind", "inSource");
                if (!string.IsNullOrWhiteSpace(finding.SuppressReason))
                {
                    json.WriteString("justification", finding.SuppressReason);
                }

                json.WriteEndObject();
                json.WriteEndArray();
            }

            json.WriteStartObject("properties");
            json.WriteString("severity", finding.Severity.ToString());
            json.WriteBoolean("inconclusive", finding.Inconclusive);
            json.WriteString("assumption", finding.Assumption);
            json.WriteString("statement", finding.StatementSummary);
            json.WriteEndObject();

            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private void WriteLocations(Utf8JsonWriter json, SourceLocation location)
    {
        json.WriteStartArray("locations");
        json.WriteStartObject();
        json.WriteStartObject("physicalLocation");

        json.WriteStartObject("artifactLocation");
        var (uri, relativeToRoot) = ArtifactUri(location.File);
        json.WriteString("uri", uri);
        if (relativeToRoot)
        {
            json.WriteString("uriBaseId", SourceRootId);
        }

        json.WriteEndObject();

        // SARIF regions are 1-based; a finding without a precise position anchors at 1:1.
        json.WriteStartObject("region");
        json.WriteNumber("startLine", Math.Max(1, location.Line));
        json.WriteNumber("startColumn", Math.Max(1, location.Column));
        json.WriteEndObject();

        json.WriteEndObject();
        json.WriteEndObject();
        json.WriteEndArray();
    }

    /// <summary>
    /// Root-relative, '/'-separated, percent-encoded URI when the file lives under the source
    /// root; otherwise an absolute <c>file://</c> URI (and no <c>uriBaseId</c>).
    /// </summary>
    private (string Uri, bool RelativeToRoot) ArtifactUri(string file)
    {
        var fullPath = Path.GetFullPath(file, _sourceRoot);
        var relative = Path.GetRelativePath(_sourceRoot, fullPath);

        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith("../", StringComparison.Ordinal))
        {
            return (new Uri(fullPath).AbsoluteUri, false);
        }

        var segments = relative
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Select(Uri.EscapeDataString);
        return (string.Join('/', segments), true);
    }

    private static string DirectoryUri(string directory)
    {
        var withSeparator = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
        return new Uri(withSeparator).AbsoluteUri;
    }
}
