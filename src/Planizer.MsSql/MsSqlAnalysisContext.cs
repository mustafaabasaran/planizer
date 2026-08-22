using Planizer.Core;

namespace Planizer.MsSql;

/// <summary>MSSQL-specific analysis context handed to every rule.</summary>
public sealed class MsSqlAnalysisContext : IAnalysisContext
{
    public required AnalysisMode Mode { get; init; }
    public required PlanizerConfig Config { get; init; }
    public required ISchemaProvider Schema { get; init; }
    public required IStatsProvider Stats { get; init; }
    public required string AssumptionText { get; init; }

    /// <summary>
    /// All deploy-time statements across the analyzed files, in pre-order (nested IF / BEGIN-END /
    /// TRY / WHILE bodies included, module bodies excluded); indices are global.
    /// </summary>
    public required IReadOnlyList<SqlStatementInfo> Statements { get; init; }

    /// <summary>Explicit transaction scopes; built per file, so a scope never spans files.</summary>
    public required IReadOnlyList<TransactionScope> Transactions { get; init; }

    /// <summary>The DDL behavior table; rules read lock/data-movement/reversibility from here.</summary>
    public required DdlBehaviorCatalog Catalog { get; init; }

    /// <summary><c>GO</c>-separated batches across the analyzed files; indices are global like statement indices.</summary>
    public required IReadOnlyList<BatchInfo> Batches { get; init; }

    /// <summary>Feature → minimum SQL Server version table (MSSQL-VER-001).</summary>
    public required FeatureVersionCatalog Features { get; init; }

    // Lookups are built once on first use: rules call these per file / per batch / per statement,
    // and a linear scan per call made directory-sized runs quadratic (13 s in one rule alone on a
    // 125k-statement repo).
    private HashSet<int>? _inTransaction;
    private Dictionary<int, List<SqlStatementInfo>>? _byBatch;
    private Dictionary<string, List<SqlStatementInfo>>? _byFile;

    private Dictionary<int, SqlStatementInfo>? _byIndex;

    /// <summary>The statement with the given global <see cref="SqlStatementInfo.Index"/>.</summary>
    public SqlStatementInfo StatementAt(int index)
        => (_byIndex ??= Statements.ToDictionary(s => s.Index))[index];

    public bool IsInExplicitTransaction(int statementIndex)
        => (_inTransaction ??= Transactions.SelectMany(t => t.StatementIndices).ToHashSet()).Contains(statementIndex);

    /// <summary>Statements of one batch in script order; empty for an unknown batch index.</summary>
    public IEnumerable<SqlStatementInfo> StatementsInBatch(int batchIndex)
        => (_byBatch ??= Statements.GroupBy(s => s.BatchIndex).ToDictionary(g => g.Key, g => g.ToList()))
            .TryGetValue(batchIndex, out var statements) ? statements : [];

    /// <summary>Statements of one file in script order (exact path match, as given to the analyzer).</summary>
    public IEnumerable<SqlStatementInfo> StatementsInFile(string file)
        => (_byFile ??= Statements.GroupBy(s => s.Location.File, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal))
            .TryGetValue(file, out var statements) ? statements : [];
}
