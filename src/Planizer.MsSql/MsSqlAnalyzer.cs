using System.Diagnostics;
using System.Reflection;
using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql;

/// <summary>
/// The MSSQL analysis pipeline: parse → context → rules → report.
/// Rules are discovered from this assembly via reflection (subclasses of
/// <see cref="MsSqlRuleBase"/>), ordered deterministically by rule id.
/// </summary>
public sealed class MsSqlAnalyzer
{
    /// <summary>Produced by the analyzer itself (not a rule class) for every ScriptDom parse error.</summary>
    public const string ParseRuleId = "MSSQL-PARSE-001";

    /// <summary>
    /// Shared with <see cref="Rules.Failure.UnsupportedFeatureRule"/>. The analyzer produces it for
    /// syntax the target grammar rejects but a newer SQL Server grammar accepts; the rule class
    /// produces it for catalogued features. Both are "this fails on the target version".
    /// </summary>
    public const string VersionRuleId = "MSSQL-VER-001";

    /// <summary>
    /// Findings of this rule are what <see cref="ScriptSummary.IrreversibleCount"/> counts.
    /// Suppressed ones are excluded — consistent with the exit-code convention, a suppressed
    /// finding stays visible in the report but stops counting.
    /// </summary>
    private const string IrreversibleRuleId = "MSSQL-REV-001";

    private readonly DdlBehaviorCatalog _catalog = DdlBehaviorCatalog.Load();
    private readonly FeatureVersionCatalog _features = FeatureVersionCatalog.Load();
    private readonly IReadOnlyList<MsSqlRuleBase>? _rules;

    /// <param name="rules">
    /// Explicit rule set; when <c>null</c> (the default) rules are discovered via reflection.
    /// Intended for tests.
    /// </param>
    public MsSqlAnalyzer(IReadOnlyList<MsSqlRuleBase>? rules = null)
    {
        _rules = rules;
    }

    public Report Analyze(IReadOnlyList<(string Path, string Sql)> files, PlanizerConfig config)
        => Analyze(files, config, progress: null);

    /// <param name="progress">
    /// Optional progress sink, called synchronously on the analyzing thread before each file is
    /// parsed, before each rule runs and once before the report is assembled.
    /// </param>
    public Report Analyze(
        IReadOnlyList<(string Path, string Sql)> files,
        PlanizerConfig config,
        IProgress<AnalysisProgress>? progress)
    {
        var totalStart = Stopwatch.GetTimestamp();
        var parser = new MsSqlScriptParser();
        var assumption = BuildAssumption(config);

        var statements = new List<SqlStatementInfo>();
        var batches = new List<BatchInfo>();
        var transactions = new List<TransactionScope>();
        var findings = new List<Finding>();
        var grammarFindings = new List<Finding>();

        var fileNumber = 0;
        foreach (var (path, sql) in files)
        {
            progress?.Report(new AnalysisProgress(AnalysisPhase.Parsing, ++fileNumber, files.Count, path));
            var result = parser.Parse(sql, path, config.TargetVersion,
                indexOffset: statements.Count, batchIndexOffset: batches.Count);

            if (result.Errors.Count > 0
                && TryParseWithNewerGrammar(parser, sql, path, config.TargetVersion, statements.Count, batches.Count)
                    is var (acceptedBy, accepted))
            {
                // The target grammar rejects syntax a newer SQL Server accepts: a version mismatch,
                // not a broken script. Analysis continues on the newer parse so every other rule
                // still sees the statements. Azure SQL is always current, so nothing to report there.
                if (config.TargetVersion != SqlServerVersion.AzureSql)
                {
                    grammarFindings.AddRange(result.Errors
                        .Select(error => GrammarFinding(error, acceptedBy, accepted.Statements, config, assumption))
                        .OfType<Finding>());
                }

                result = accepted;
            }

            foreach (var error in result.Errors)
            {
                findings.Add(new Finding
                {
                    RuleId = ParseRuleId,
                    Severity = Severity.Blocker,
                    Message = $"Parse error: {error.Message}",
                    Location = error.Location,
                    Assumption = assumption,
                });
            }

            // Per file on purpose: a transaction can never span files.
            transactions.AddRange(TransactionScopeBuilder.Build(result.Statements));
            statements.AddRange(result.Statements);
            batches.AddRange(result.Batches);
        }

        var context = new MsSqlAnalysisContext
        {
            Mode = AnalysisMode.Offline,
            Config = config,
            Schema = UnavailableSchemaProvider.Instance,
            Stats = UnavailableStatsProvider.Instance,
            AssumptionText = assumption,
            Statements = statements,
            Transactions = transactions,
            Catalog = _catalog,
            Batches = batches,
            Features = _features,
        };

        var parseMs = Stopwatch.GetElapsedTime(totalStart).TotalMilliseconds;
        var rulesStart = Stopwatch.GetTimestamp();
        var timings = new List<RuleTiming>();

        // Disabled rules are neither run nor counted — the progress total is what will actually execute.
        var enabledRules = (_rules ?? DiscoverRules())
            .Select(rule => (Rule: rule, Override: config.Rules.TryGetValue(rule.Id, out var o) ? o : null))
            .Where(r => r.Override is not { Enabled: false })
            .ToList();

        var ruleNumber = 0;
        foreach (var (rule, ruleOverride) in enabledRules)
        {
            progress?.Report(new AnalysisProgress(AnalysisPhase.Rules, ++ruleNumber, enabledRules.Count, rule.Id));
            var ruleStart = Stopwatch.GetTimestamp();
            var before = findings.Count;

            foreach (var finding in rule.Analyze((IAnalysisContext)context))
            {
                findings.Add(ruleOverride?.Severity is { } severity
                    ? finding with { Severity = severity }
                    : finding);
            }

            timings.Add(new RuleTiming(rule.Id, Stopwatch.GetElapsedTime(ruleStart).TotalMilliseconds, findings.Count - before));
        }

        var rulesMs = Stopwatch.GetElapsedTime(rulesStart).TotalMilliseconds;
        progress?.Report(new AnalysisProgress(AnalysisPhase.Finishing, 1, 1, "summary"));

        // A catalog finding names the feature and its minimum version; the grammar finding on the
        // same statement would only repeat "incorrect syntax", so the more specific one wins.
        var ruleVersionLocations = findings
            .Where(f => f.RuleId == VersionRuleId)
            .Select(f => f.Location)
            .ToHashSet();
        findings.AddRange(grammarFindings.Where(g => !ruleVersionLocations.Contains(g.Location)));

        var resolved = ApplySuppressions(findings, statements);

        return new Report
        {
            ToolVersion = GetToolVersion(),
            Dialect = config.Dialect,
            TargetVersion = TargetParser.VersionToken(config.TargetVersion),
            Edition = TargetParser.EditionToken(config.Edition),
            Mode = AnalysisMode.Offline,
            Files = files.Select(f => f.Path).ToList(),
            Findings = resolved,
            Summary = BuildSummary(statements, config, resolved),
            SuppressedCount = resolved.Count(f => f.Suppressed),
            Timing = new AnalysisTiming
            {
                ParseMs = parseMs,
                RulesMs = rulesMs,
                TotalMs = Stopwatch.GetElapsedTime(totalStart).TotalMilliseconds,
                Rules = timings,
            },
        };
    }

    /// <summary>
    /// Re-parses a file the target grammar rejected with each newer grammar, oldest first, and
    /// returns the first that accepts it without errors; <c>null</c> when none does (the script
    /// is genuinely broken → MSSQL-PARSE-001).
    /// </summary>
    private static (SqlGrammar Grammar, MsSqlParseResult Result)? TryParseWithNewerGrammar(
        MsSqlScriptParser parser,
        string sql,
        string path,
        SqlServerVersion targetVersion,
        int indexOffset,
        int batchIndexOffset)
    {
        foreach (var grammar in SqlGrammar.NewerThan(targetVersion))
        {
            var retry = parser.Parse(sql, path, grammar, indexOffset, batchIndexOffset);
            if (retry.Errors.Count == 0)
            {
                return (grammar, retry);
            }
        }

        return null;
    }

    /// <summary>
    /// MSSQL-VER-001 for a parse error that vanishes under a newer grammar. Anchored to the
    /// innermost statement (of the accepted parse) that contains the offending token, so it groups
    /// and suppresses like any statement finding. Honours a <c>.planizer.json</c> override of the
    /// rule like any rule finding (<c>null</c> when disabled).
    /// </summary>
    private static Finding? GrammarFinding(
        MsSqlParseError error,
        SqlGrammar acceptedBy,
        IReadOnlyList<SqlStatementInfo> statements,
        PlanizerConfig config,
        string assumption)
    {
        config.Rules.TryGetValue(VersionRuleId, out var ruleOverride);
        if (ruleOverride is { Enabled: false })
        {
            return null;
        }

        var statement = statements
            .Where(s => Contains(s, error.Location))
            .OrderByDescending(s => s.Index) // pre-order: the innermost / latest-starting container wins
            .FirstOrDefault();

        var target = TargetParser.VersionToken(config.TargetVersion);
        var fix = acceptedBy.IsTargetVersion
            ? $"Raise --target-version to {acceptedBy.Label} if production really runs SQL Server {acceptedBy.Label} " +
              $"or newer; otherwise rewrite the statement with syntax SQL Server {target} accepts."
            : $"The syntax needs a SQL Server newer than 2022 ({acceptedBy.Label} grammar); rewrite the statement " +
              $"with syntax SQL Server {target} accepts.";

        return new Finding
        {
            RuleId = VersionRuleId,
            Severity = ruleOverride?.Severity ?? Severity.Blocker,
            Message = $"Syntax not supported by the SQL Server {target} grammar; it first parses with the " +
                      $"SQL Server {acceptedBy.Label} grammar: {error.Message}",
            Fix = fix,
            Location = statement?.Location ?? error.Location,
            StatementSummary = statement is null ? null : Summarize(statement.Sql),
            Assumption = assumption,
        };
    }

    /// <summary>Whether the statement's source span (start token … last token) contains the location.</summary>
    private static bool Contains(SqlStatementInfo statement, SourceLocation location)
    {
        var start = statement.Location;
        if (!string.Equals(start.File, location.File, StringComparison.Ordinal))
        {
            return false;
        }

        var startsBefore = start.Line < location.Line
            || (start.Line == location.Line && start.Column <= location.Column);
        return startsBefore && location.Line <= EndLine(statement);
    }

    /// <summary>First ~120 characters of the statement, whitespace collapsed (same shape as rule findings).</summary>
    private static string Summarize(string sql)
    {
        var collapsed = string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= 120 ? collapsed : collapsed[..120];
    }

    private static int EndLine(SqlStatementInfo statement)
    {
        var ast = statement.Ast;
        return ast.ScriptTokenStream is { } tokens && ast.LastTokenIndex >= 0 && ast.LastTokenIndex < tokens.Count
            ? tokens[ast.LastTokenIndex].Line
            : statement.Location.Line;
    }

    /// <summary>
    /// Marks findings whose statement carries a matching <c>planizer:ignore</c> as suppressed.
    /// Suppressed findings stay in the report; they only stop counting toward the exit code.
    /// Findings are matched to statements by exact source location.
    /// </summary>
    private static List<Finding> ApplySuppressions(
        List<Finding> findings,
        IReadOnlyList<SqlStatementInfo> statements)
    {
        var suppressing = statements
            .Where(s => s.SuppressedRuleIds.Count > 0)
            .ToLookup(s => s.Location);

        return findings
            .Select(finding =>
            {
                var match = suppressing[finding.Location]
                    .FirstOrDefault(s => s.SuppressedRuleIds.Contains(finding.RuleId));
                return match is null
                    ? finding
                    : finding with { Suppressed = true, SuppressReason = match.SuppressReason };
            })
            .ToList();
    }

    private ScriptSummary BuildSummary(
        IReadOnlyList<SqlStatementInfo> statements,
        PlanizerConfig config,
        IReadOnlyList<Finding> findings)
    {
        // Reverse statements are collected in script order, then flipped: a rollback undoes
        // the last change first. Complete only when every state-changing statement reversed.
        var rollback = new List<string>();
        bool? rollbackComplete = config.Rollback ? true : null;

        foreach (var statement in config.Rollback
                     ? statements.Where(Rules.Reversibility.RollbackScriptBuilder.RequiresRollback)
                     : [])
        {
            if (Rules.Reversibility.RollbackScriptBuilder.TryReverse(statement) is { } reverse)
            {
                rollback.Add(reverse);
            }
            else
            {
                rollbackComplete = false;
            }
        }

        rollback.Reverse();

        return new()
        {
            StatementCount = statements.Count,
            DdlCount = statements.Count(s => s.Kind == StatementKind.Ddl),
            SchMLockCount = statements.Count(s => DdlOperationClassifier.AcquiresSchMLock(s, _catalog, config)),
            IrreversibleCount = findings.Count(f => f.RuleId == IrreversibleRuleId && !f.Suppressed),
            UnanalyzableCount = statements.Count(s => s.Kind == StatementKind.Dynamic),
            RollbackScript = rollback,
            RollbackComplete = rollbackComplete,
        };
    }

    private static string BuildAssumption(PlanizerConfig config)
        => $"SQL Server {TargetParser.VersionToken(config.TargetVersion)}, " +
           $"{TargetParser.EditionToken(config.Edition)} edition, offline mode";

    /// <summary>All rules in this assembly, ordered by rule id (also used by <c>planizer rules</c>).</summary>
    public static IReadOnlyList<MsSqlRuleBase> DiscoverRules()
        => typeof(MsSqlAnalyzer).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract
                && t.IsAssignableTo(typeof(MsSqlRuleBase))
                && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (MsSqlRuleBase)Activator.CreateInstance(t)!)
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

    private static string GetToolVersion()
    {
        var assembly = typeof(MsSqlAnalyzer).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var metadataStart = informational.IndexOf('+');
            return metadataStart >= 0 ? informational[..metadataStart] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
