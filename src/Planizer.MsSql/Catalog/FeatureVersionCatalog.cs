using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Rules.Locking;

namespace Planizer.MsSql;

/// <summary>How a catalogued feature is recognised in a parsed script.</summary>
public enum FeatureDetection
{
    /// <summary>A built-in function call by name (scalar or table-valued), optionally with a minimum argument count.</summary>
    Function,

    /// <summary>An AST node type, e.g. <c>AtTimeZoneCall</c> or <c>CreateOrAlterProcedureStatement</c>.</summary>
    Statement,

    /// <summary>An index option in context, identified by one of the keys <see cref="FeatureVersionCatalog"/> computes.</summary>
    Option,

    /// <summary>
    /// Pure syntax the older grammar rejects; not detected from the AST. The analyzer's grammar
    /// re-parse path reports it (MSSQL-VER-001 instead of MSSQL-PARSE-001). Listed for documentation.
    /// </summary>
    Syntax,
}

/// <summary>A T-SQL feature and the first SQL Server version that supports it.</summary>
public sealed record FeatureVersion(
    string FeatureKey,
    FeatureDetection Detection,
    string Pattern,
    SqlServerVersion MinVersion,
    bool RequiresServicePack1,
    string? Note)
{
    /// <summary>Minimum number of arguments for <see cref="FeatureDetection.Function"/> rows written as <c>NAME/N</c>; 0 otherwise.</summary>
    public int MinArguments { get; init; }

    /// <summary>Bare function name for function rows (pattern without the arity suffix).</summary>
    public string FunctionName { get; init; } = "";

    /// <summary>User-facing minimum version: "2017", or "2016 SP1".</summary>
    public string MinVersionLabel
        => RequiresServicePack1 ? $"{TargetParser.VersionToken(MinVersion)} SP1" : TargetParser.VersionToken(MinVersion);

    /// <summary>Human name of the feature for messages, e.g. "STRING_AGG()", "LTRIM() with 2 arguments", "CREATE OR ALTER PROCEDURE".</summary>
    public string Label => Detection switch
    {
        FeatureDetection.Function when MinArguments > 0 => $"{FunctionName}() with {MinArguments} arguments",
        FeatureDetection.Function => $"{FunctionName}()",
        _ => FeatureKey.Replace('_', ' ').ToUpperInvariant(),
    };

    /// <summary>
    /// Whether the target guarantees the feature. Azure SQL is always current. A bare 2016 target
    /// does not satisfy a 2016 SP1 feature: the patch level is unknown offline.
    /// </summary>
    public bool IsAvailableOn(SqlServerVersion target)
        => target == SqlServerVersion.AzureSql
           || FeatureVersionCatalog.Rank(target) >= FeatureVersionCatalog.Rank(MinVersion, RequiresServicePack1);
}

/// <summary>One occurrence of a catalogued feature in a script.</summary>
public readonly record struct FeatureUse(FeatureVersion Feature, TSqlFragment Fragment);

/// <summary>
/// Feature → minimum SQL Server version table (<c>Catalog/mssql-feature-versions.csv</c>),
/// consumed by MSSQL-VER-001. Function names are matched case-insensitively on bare built-in
/// calls only (a schema-qualified call is a user-defined function whatever its name); module
/// bodies are scanned too, because a procedure referencing an unknown function fails at CREATE.
/// </summary>
public sealed class FeatureVersionCatalog
{
    /// <summary>Option key: <c>ALTER INDEX … REBUILD WITH (RESUMABLE = ON)</c>.</summary>
    public const string ResumableIndexRebuild = "resumable_index_rebuild";

    /// <summary>Option key: <c>CREATE INDEX … WITH (RESUMABLE = ON)</c>.</summary>
    public const string ResumableCreateIndex = "resumable_create_index";

    /// <summary>Option key: <c>CREATE INDEX … WITH (ONLINE = ON (WAIT_AT_LOW_PRIORITY (…)))</c>.</summary>
    public const string WaitAtLowPriorityCreateIndex = "wait_at_low_priority_create_index";

    /// <summary>Option key: <c>ALTER INDEX … REBUILD WITH (ONLINE = ON (WAIT_AT_LOW_PRIORITY (…)))</c>.</summary>
    public const string WaitAtLowPriorityIndexRebuild = "wait_at_low_priority_index_rebuild";

    private const string ResourceName = "Planizer.MsSql.Catalog.mssql-feature-versions.csv";
    private const string Header = "feature_key,detect,pattern,min_version,note";

    private static readonly Lazy<FeatureVersionCatalog> Embedded = new(LoadEmbedded);

    private readonly Dictionary<string, FeatureVersion> _byKey;
    private readonly ILookup<string, FeatureVersion> _functions;
    private readonly ILookup<string, FeatureVersion> _statements;
    private readonly ILookup<string, FeatureVersion> _options;

    private FeatureVersionCatalog(IReadOnlyList<FeatureVersion> features)
    {
        _byKey = features.ToDictionary(f => f.FeatureKey, StringComparer.OrdinalIgnoreCase);
        _functions = features
            .Where(f => f.Detection == FeatureDetection.Function)
            .ToLookup(f => f.FunctionName, StringComparer.OrdinalIgnoreCase);
        _statements = features
            .Where(f => f.Detection == FeatureDetection.Statement)
            .ToLookup(f => f.Pattern, StringComparer.Ordinal);
        _options = features
            .Where(f => f.Detection == FeatureDetection.Option)
            .ToLookup(f => f.Pattern, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every row of the table.</summary>
    public IReadOnlyCollection<FeatureVersion> Features => _byKey.Values;

    /// <summary>Loads the embedded table. The parsed table is cached process-wide.</summary>
    public static FeatureVersionCatalog Load() => Embedded.Value;

    /// <summary>Resolves a feature by key (case-insensitive); <c>null</c> when the table has no row for it.</summary>
    public FeatureVersion? Lookup(string featureKey)
        => _byKey.TryGetValue(featureKey, out var feature) ? feature : null;

    /// <summary>Every use of a catalogued feature anywhere in the fragment, in visiting order.</summary>
    public IEnumerable<FeatureUse> FindUses(TSqlFragment fragment)
    {
        var collector = new UseCollector(this);
        fragment.Accept(collector);
        return collector.Uses;
    }

    /// <summary>
    /// Uses of features the target does not guarantee (see <see cref="FeatureVersion.IsAvailableOn"/>).
    /// One result per fragment: when several rows match the same call (<c>STRING_SPLIT</c> and
    /// its 3-argument form) the most demanding one is reported.
    /// </summary>
    public IEnumerable<FeatureUse> FindViolations(TSqlFragment fragment, SqlServerVersion target)
        => FindUses(fragment)
            .Where(u => !u.Feature.IsAvailableOn(target))
            .GroupBy(u => u.Fragment)
            .Select(g => g
                .OrderByDescending(u => Rank(u.Feature.MinVersion, u.Feature.RequiresServicePack1))
                .First());

    /// <summary>Parses CSV text; exposed for tests. Line 1 must be the header.</summary>
    public static FeatureVersionCatalog Parse(string csv)
    {
        var lines = csv.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0 || lines[0] != Header)
        {
            throw new InvalidDataException("Feature version CSV is missing its expected header line.");
        }

        return new FeatureVersionCatalog(lines.Skip(1).Select(ParseRow).ToList());
    }

    private static FeatureVersionCatalog LoadEmbedded()
    {
        using var stream = typeof(FeatureVersionCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static FeatureVersion ParseRow(string line)
    {
        // Fields hold no commas by convention (notes use ';'); a 5-way split keeps any stray
        // comma inside the trailing note intact.
        var fields = line.Split(',', 5);
        if (fields.Length != 5)
        {
            throw new InvalidDataException($"Feature version CSV row has too few fields: '{line}'.");
        }

        var detection = fields[1] switch
        {
            "function" => FeatureDetection.Function,
            "statement" => FeatureDetection.Statement,
            "option" => FeatureDetection.Option,
            "syntax" => FeatureDetection.Syntax,
            _ => throw new InvalidDataException($"Unknown detect '{fields[1]}' in row '{line}'."),
        };

        var (minVersion, requiresSp1) = fields[3] switch
        {
            "2014" => (SqlServerVersion.Sql2014, false),
            "2016" => (SqlServerVersion.Sql2016, false),
            "2016sp1" => (SqlServerVersion.Sql2016, true),
            "2017" => (SqlServerVersion.Sql2017, false),
            "2019" => (SqlServerVersion.Sql2019, false),
            "2022" => (SqlServerVersion.Sql2022, false),
            _ => throw new InvalidDataException($"Unknown min_version '{fields[3]}' in row '{line}'."),
        };

        var pattern = fields[2];
        var functionName = pattern;
        var minArguments = 0;

        if (detection == FeatureDetection.Function)
        {
            var slash = pattern.IndexOf('/');
            if (slash >= 0)
            {
                functionName = pattern[..slash];
                if (!int.TryParse(pattern[(slash + 1)..], out minArguments) || minArguments < 1)
                {
                    throw new InvalidDataException($"Invalid function arity in pattern '{pattern}' (row '{line}').");
                }
            }
        }

        return new FeatureVersion(
            fields[0],
            detection,
            pattern,
            minVersion,
            requiresSp1,
            string.IsNullOrWhiteSpace(fields[4]) ? null : fields[4])
        {
            FunctionName = functionName,
            MinArguments = minArguments,
        };
    }

    /// <summary>Ordering of versions with 2016 SP1 between 2016 RTM and 2017; Azure ranks above everything.</summary>
    internal static int Rank(SqlServerVersion version, bool servicePack1 = false) => version switch
    {
        SqlServerVersion.Sql2014 => 10,
        SqlServerVersion.Sql2016 => servicePack1 ? 25 : 20,
        SqlServerVersion.Sql2017 => 30,
        SqlServerVersion.Sql2019 => 40,
        SqlServerVersion.Sql2022 => 50,
        SqlServerVersion.AzureSql => int.MaxValue,
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown SQL Server version."),
    };

    /// <summary>
    /// Walks a fragment and records every catalogued feature. Typed overrides call the base so the
    /// untyped <see cref="Visit(TSqlFragment)"/> (statement-type matching) still runs for them.
    /// </summary>
    private sealed class UseCollector(FeatureVersionCatalog catalog) : TSqlFragmentVisitor
    {
        public List<FeatureUse> Uses { get; } = [];

        public override void Visit(TSqlFragment node)
        {
            foreach (var feature in catalog._statements[node.GetType().Name])
            {
                Uses.Add(new FeatureUse(feature, node));
            }
        }

        public override void Visit(FunctionCall node)
        {
            base.Visit(node);
            if (node.CallTarget is null && node.FunctionName?.Value is { } name)
            {
                MatchFunction(name, node.Parameters.Count, node);
            }
        }

        public override void Visit(GlobalFunctionTableReference node)
        {
            base.Visit(node);
            if (node.Name?.Value is { } name)
            {
                MatchFunction(name, node.Parameters.Count, node);
            }
        }

        public override void Visit(SchemaObjectFunctionTableReference node)
        {
            base.Visit(node);

            // Older grammars (2014) parse built-in table-valued functions as schema-object calls;
            // only an unqualified name can be a built-in — dbo.STRING_SPLIT is a user function.
            if (node.SchemaObject is { Identifiers.Count: 1 } schemaObject)
            {
                MatchFunction(schemaObject.BaseIdentifier.Value, node.Parameters.Count, node);
            }
        }

        public override void Visit(CreateIndexStatement node)
        {
            base.Visit(node);
            if (IndexOptionInspector.IsResumable(node.IndexOptions))
            {
                MatchOption(ResumableCreateIndex, node);
            }

            if (IndexOptionInspector.HasWaitAtLowPriority(node.IndexOptions))
            {
                MatchOption(WaitAtLowPriorityCreateIndex, node);
            }
        }

        public override void Visit(AlterIndexStatement node)
        {
            base.Visit(node);
            if (node.AlterIndexType != AlterIndexType.Rebuild)
            {
                return;
            }

            if (IndexOptionInspector.IsResumable(node.IndexOptions))
            {
                MatchOption(ResumableIndexRebuild, node);
            }

            if (IndexOptionInspector.HasWaitAtLowPriority(node.IndexOptions))
            {
                MatchOption(WaitAtLowPriorityIndexRebuild, node);
            }
        }

        private void MatchFunction(string name, int argumentCount, TSqlFragment node)
        {
            foreach (var feature in catalog._functions[name])
            {
                if (argumentCount >= feature.MinArguments)
                {
                    Uses.Add(new FeatureUse(feature, node));
                }
            }
        }

        private void MatchOption(string optionKey, TSqlFragment node)
        {
            foreach (var feature in catalog._options[optionKey])
            {
                Uses.Add(new FeatureUse(feature, node));
            }
        }
    }
}
