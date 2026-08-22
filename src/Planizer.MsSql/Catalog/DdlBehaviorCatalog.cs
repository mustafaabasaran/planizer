using Planizer.Core;

namespace Planizer.MsSql;

/// <summary>
/// The heart of Planizer: the DDL behavior table (<c>statement × version × edition → lock,
/// data movement, reversibility</c>), loaded from the embedded
/// <c>Catalog/mssql-ddl-behavior.csv</c>.
///
/// Lookup picks the most specific applicable row: an edition-specific row beats an
/// <c>any</c> row, and among those the highest applicable <c>min_version</c> wins.
/// No applicable row → <c>null</c> → the calling rule reports an inconclusive finding
/// instead of guessing.
/// </summary>
public sealed class DdlBehaviorCatalog
{
    private const string ResourceName = "Planizer.MsSql.Catalog.mssql-ddl-behavior.csv";

    private static readonly Lazy<DdlBehaviorCatalog> Embedded = new(LoadEmbedded);

    private readonly ILookup<string, Row> _rows;

    private DdlBehaviorCatalog(IEnumerable<Row> rows)
        => _rows = rows.ToLookup(r => r.OperationKey, StringComparer.Ordinal);

    /// <summary>Operation keys present in the table (distinct, unordered).</summary>
    public IReadOnlyCollection<string> OperationKeys => _rows.Select(g => g.Key).ToList();

    /// <summary>Loads the catalog from the embedded CSV. The parsed table is cached process-wide.</summary>
    public static DdlBehaviorCatalog Load() => Embedded.Value;

    /// <summary>
    /// Resolves the behavior of an operation under the given target; <c>null</c> when no row
    /// applies (unknown key, or every row demands a different edition / newer version).
    /// </summary>
    public DdlBehavior? Lookup(string operationKey, SqlServerVersion version, SqlEdition edition)
    {
        var editionToken = EditionToken(edition);
        var versionRank = VersionRank(version);

        return _rows[operationKey]
            .Where(r => (r.Edition == Row.AnyEdition || r.Edition == editionToken)
                && r.MinVersionRank <= versionRank)
            .OrderByDescending(r => r.Edition != Row.AnyEdition) // edition-specific beats "any"
            .ThenByDescending(r => r.MinVersionRank)             // highest applicable min_version wins
            .Select(r => r.Behavior)
            .FirstOrDefault();
    }

    /// <summary>Parses CSV text; exposed for tests. Line 1 must be the header.</summary>
    public static DdlBehaviorCatalog Parse(string csv)
    {
        var lines = csv.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0 || lines[0] != "operation_key,edition,min_version,lock,data_movement,reversible,notes")
        {
            throw new InvalidDataException("DDL behavior CSV is missing its expected header line.");
        }

        return new DdlBehaviorCatalog(lines.Skip(1).Select(ParseRow));
    }

    private static DdlBehaviorCatalog LoadEmbedded()
    {
        using var stream = typeof(DdlBehaviorCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static Row ParseRow(string line)
    {
        // Fields hold no commas by convention (notes use ';'); a 7-way split still keeps any
        // stray comma inside the trailing notes field intact.
        var fields = line.Split(',', 7);
        if (fields.Length != 7)
        {
            throw new InvalidDataException($"DDL behavior CSV row has too few fields: '{line}'.");
        }

        var edition = fields[1] switch
        {
            Row.AnyEdition or "enterprise" or "standard_express" => fields[1],
            _ => throw new InvalidDataException($"Unknown edition '{fields[1]}' in row '{line}'."),
        };

        var behavior = new DdlBehavior(
            Lock: fields[3] switch
            {
                "schm" => LockLevel.SchM,
                "schm_brief" => LockLevel.SchMBrief,
                "s_table" => LockLevel.STable,
                "none" => LockLevel.None,
                _ => throw new InvalidDataException($"Unknown lock '{fields[3]}' in row '{line}'."),
            },
            Movement: fields[4] switch
            {
                "metadata_only" => DataMovement.MetadataOnly,
                "rewrite" => DataMovement.Rewrite,
                "full_scan" => DataMovement.FullScan,
                "index_build" => DataMovement.IndexBuild,
                "fails_if_rows" => DataMovement.FailsIfRows,
                "deallocate" => DataMovement.Deallocate,
                "none" => DataMovement.None,
                _ => throw new InvalidDataException($"Unknown data_movement '{fields[4]}' in row '{line}'."),
            },
            Reversible: fields[5] switch
            {
                "yes" => Reversibility.Yes,
                "no" => Reversibility.No,
                "with_script" => Reversibility.WithScript,
                _ => throw new InvalidDataException($"Unknown reversible '{fields[5]}' in row '{line}'."),
            },
            Notes: string.IsNullOrWhiteSpace(fields[6]) ? null : fields[6]);

        return new Row(fields[0], edition, MinVersionRank(fields[0], fields[2], line), behavior);
    }

    private static int MinVersionRank(string operationKey, string minVersion, string line) => minVersion switch
    {
        "any" => 0,
        "2014" => 10,
        "2016" => 20,
        // 2016 SP1 sits between 2016 RTM and 2017; a plain "2016" target conservatively
        // does NOT satisfy it (patch level is unknown offline).
        "2016sp1" => 25,
        "2017" => 30,
        "2019" => 40,
        "2022" => 50,
        _ => throw new InvalidDataException($"Unknown min_version '{minVersion}' in row '{line}' ({operationKey})."),
    };

    private static int VersionRank(SqlServerVersion version) => version switch
    {
        SqlServerVersion.Sql2014 => 10,
        SqlServerVersion.Sql2016 => 20,
        SqlServerVersion.Sql2017 => 30,
        SqlServerVersion.Sql2019 => 40,
        SqlServerVersion.Sql2022 => 50,
        SqlServerVersion.AzureSql => int.MaxValue, // Azure is always current
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown SQL Server version."),
    };

    /// <summary>CSV edition tokens: Azure behaves like Enterprise; Developer is mapped to Enterprise upstream.</summary>
    private static string EditionToken(SqlEdition edition) => edition switch
    {
        SqlEdition.Enterprise or SqlEdition.Azure => "enterprise",
        SqlEdition.Standard or SqlEdition.Express => "standard_express",
        _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown SQL Server edition."),
    };

    private sealed record Row(string OperationKey, string Edition, int MinVersionRank, DdlBehavior Behavior)
    {
        public const string AnyEdition = "any";
    }
}
