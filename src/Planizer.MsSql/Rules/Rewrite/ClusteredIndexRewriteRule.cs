using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-013: the clustered index IS the table — creating one on a heap, or dropping the
/// existing one, rewrites every row and rebuilds every nonclustered index (their row locators
/// change). Plain CREATE CLUSTERED INDEX is certain: it only succeeds when the table is
/// currently a heap. WITH (DROP_EXISTING = ON) also succeeds on a table that already has a
/// clustered index — still a full rewrite, but nonclustered indexes are rebuilt only when the
/// clustering key changes, so the message differs. DROP INDEX is decided from the script when
/// the same file created that index earlier (EF Core scripts accumulate every migration, so
/// this is common): a known clustered index is a certain rewrite, a known nonclustered one is
/// not reported. Otherwise the script alone cannot tell whether the index is clustered, and the
/// rule reports an inconclusive Warning instead of staying silent.
/// </summary>
public sealed class ClusteredIndexRewriteRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-RW-013";
    public override string Title => "Creating or dropping a clustered index rewrites the entire table";
    public override Severity DefaultSeverity => Severity.Critical;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            switch (statement.Ast)
            {
                case CreateIndexStatement { Clustered: true } create:
                    yield return AnalyzeCreate(context, statement, create);
                    break;

                case DropIndexStatement drop:
                    foreach (var finding in AnalyzeDrop(context, statement, drop))
                    {
                        yield return finding;
                    }

                    break;
            }
        }
    }

    private Finding AnalyzeCreate(
        MsSqlAnalysisContext context, SqlStatementInfo statement, CreateIndexStatement create)
    {
        var index = create.Name?.Value ?? "the clustered index";
        var table = SqlNames.Render(create.OnName, "the table");
        var behavior = context.Catalog.Lookup(
            DdlOperationKeys.CreateClusteredIndexOnHeap, context.Config.TargetVersion, context.Config.Edition);

        if (behavior is null)
        {
            return CreateFinding(statement, Severity.Warning,
                $"Creating clustered index {index} on {table}: behavior is not cataloged for this " +
                "target — review manually.",
                inconclusive: true);
        }

        if (HasDropExisting(create))
        {
            return CreateFinding(statement, DefaultSeverity,
                $"Creating clustered index {index} WITH (DROP_EXISTING = ON) recreates the " +
                $"clustered index of {table}, rewriting every row; nonclustered indexes are " +
                "rebuilt only if the clustering key changes.");
        }

        return CreateFinding(statement, DefaultSeverity,
            $"Creating clustered index {index} rewrites every row of {table} and rebuilds all of " +
            "its nonclustered indexes (without DROP_EXISTING the statement only succeeds on a heap).");
    }

    private static bool HasDropExisting(CreateIndexStatement create)
        => create.IndexOptions.OfType<IndexStateOption>()
            .Any(o => o.OptionKind == IndexOptionKind.DropExisting && o.OptionState == OptionState.On);

    private IEnumerable<Finding> AnalyzeDrop(
        MsSqlAnalysisContext context, SqlStatementInfo statement, DropIndexStatement drop)
    {
        foreach (var clause in drop.DropIndexClauses)
        {
            var (index, table) = clause switch
            {
                DropIndexClause modern => (
                    modern.Index?.Value ?? "the index",
                    SqlNames.Render(modern.Object, "its table")),
                BackwardsCompatibleDropIndexClause legacy => (
                    legacy.Index?.ChildIdentifier?.Value ?? "the index",
                    "its table"),
                _ => ("the index", "its table"),
            };

            var created = clause is DropIndexClause { Index.Value: { } indexName, Object: { } tableName }
                ? FindEarlierCreate(context, statement, indexName, tableName)
                : null;

            if (created is { Clustered: true })
            {
                yield return CreateFinding(statement, DefaultSeverity,
                    $"Dropping clustered index {index} (created earlier in this file) turns {table} " +
                    "back into a heap, rewriting every row and rebuilding all of its nonclustered indexes.");
                continue;
            }

            if (created is not null)
            {
                continue; // known nonclustered: pages are deallocated, no row is rewritten
            }

            yield return CreateFinding(statement, Severity.Warning,
                $"Cannot determine offline whether {index} is the clustered index of {table}; " +
                "dropping a clustered index turns the table back into a heap, rewriting every row " +
                "and rebuilding all nonclustered indexes — verify before running.",
                inconclusive: true);
        }
    }

    /// <summary>
    /// The CREATE INDEX of the same name on the same table that precedes the drop in the same
    /// file, if any. Only earlier statements count: a CREATE after the DROP (drop-and-recreate)
    /// says nothing certain about the index being dropped.
    /// </summary>
    private static CreateIndexStatement? FindEarlierCreate(
        MsSqlAnalysisContext context, SqlStatementInfo drop, string indexName, SchemaObjectName table)
    {
        var tableKey = TableNames.Key(table);
        return context.StatementsInFile(drop.Location.File)
            .Where(s => s.Index < drop.Index)
            .Select(s => s.Ast as CreateIndexStatement)
            .LastOrDefault(c => c is not null
                && string.Equals(c.Name?.Value, indexName, StringComparison.OrdinalIgnoreCase)
                && TableNames.Key(c.OnName) == tableKey);
    }
}
