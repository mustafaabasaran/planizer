using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;
using Planizer.MsSql.Rules.Locking;

namespace Planizer.MsSql.Rules.Failure;

/// <summary>
/// MSSQL-LIM-001: an index, PRIMARY KEY or UNIQUE key that breaks SQL Server's key limits.
/// <list type="bullet">
/// <item><b>Column count</b> — more than 32 key columns (16 before SQL Server 2016) → Blocker,
/// error 1904 at CREATE. Needs no type information.</item>
/// <item><b>LOB / MAX key column</b> — varchar(max), xml, text, … can never be a key column →
/// Blocker, error 1919.</item>
/// <item><b>Key size</b> — 900 bytes for a clustered key, 1700 for a nonclustered key since
/// SQL Server 2016 (900 before). When the fixed-width columns alone exceed the limit CREATE
/// fails (error 1944) → Blocker. When only the declared maximum of variable-length columns
/// exceeds it, CREATE succeeds with a warning and the first INSERT/UPDATE producing a longer key
/// fails (error 1946) → Critical.</item>
/// </list>
/// Column types are resolved from <c>CREATE TABLE</c> and <c>ALTER TABLE … ADD</c> statements in
/// the same file; when a key column's type is unknown the byte checks stay silent rather than
/// reporting inconclusive on every index of a migration.
/// </summary>
public sealed class IndexKeyLimitRule : MsSqlRuleBase
{
    private const int ClusteredMaxBytes = 900;
    private const int NonclusteredMaxBytesLegacy = 900;
    private const int NonclusteredMaxBytes2016 = 1700;
    private const int MaxKeyColumnsLegacy = 16;
    private const int MaxKeyColumns2016 = 32;

    public override string Id => "MSSQL-LIM-001";
    public override string Title => "Index key exceeds the column-count or byte-size limit";
    public override Severity DefaultSeverity => Severity.Blocker;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        var modern = context.Config.TargetVersion >= SqlServerVersion.Sql2016;
        var maxColumns = modern ? MaxKeyColumns2016 : MaxKeyColumnsLegacy;
        var nonclusteredMaxBytes = modern ? NonclusteredMaxBytes2016 : NonclusteredMaxBytesLegacy;
        var targetLabel = TargetParser.VersionToken(context.Config.TargetVersion);

        foreach (var file in context.Statements.GroupBy(s => s.Location.File, StringComparer.Ordinal))
        {
            var columnTypes = CollectColumnTypes(file);

            foreach (var statement in file)
            {
                foreach (var key in Keys(statement.Ast))
                {
                    var table = IndexOptionInspector.Render(key.Table);
                    if (key.Columns.Count > maxColumns)
                    {
                        yield return CreateFinding(statement, DefaultSeverity,
                            $"{key.Name} on {table} has {key.Columns.Count} key columns; SQL Server {targetLabel} " +
                            $"allows at most {maxColumns} (error 1904 at CREATE time).",
                            "Keep only the columns that are searched or sorted in the key and move the rest to " +
                            "INCLUDE (nonclustered indexes), or split the index.");
                        continue;
                    }

                    if (TableNames.Key(key.Table) is not { } tableKey
                        || !columnTypes.TryGetValue(tableKey, out var types))
                    {
                        continue; // table not defined in this file: key width unknown offline
                    }

                    var lobColumns = new List<string>();
                    var resolvable = true;
                    var fixedBytes = 0;
                    var maxBytes = 0;

                    foreach (var column in key.Columns)
                    {
                        if (!types.TryGetValue(column, out var type) || type is null)
                        {
                            resolvable = false; // computed column or not declared here
                            continue;
                        }

                        if (SqlTypeWidths.IsLargeObject(type))
                        {
                            lobColumns.Add($"{column} ({SqlTypeWidths.Describe(type)})");
                            continue;
                        }

                        if (SqlTypeWidths.MaxKeyBytes(type) is not { } width)
                        {
                            resolvable = false; // sql_variant, CLR, alias types
                            continue;
                        }

                        maxBytes += width;
                        fixedBytes += SqlTypeWidths.FixedWidthBytes(type) ?? 0;
                    }

                    if (lobColumns.Count > 0)
                    {
                        var plural = lobColumns.Count > 1;
                        yield return CreateFinding(statement, DefaultSeverity,
                            $"Key column{(plural ? "s" : "")} {string.Join(", ", lobColumns)} of {key.Name} on {table} " +
                            $"{(plural ? "use" : "uses")} a LOB/MAX type, which cannot be an index key column (error 1919).",
                            "Move the column to INCLUDE, or index a bounded computed column such as a LEFT(…, 450) prefix.");
                        continue;
                    }

                    if (!resolvable)
                    {
                        continue;
                    }

                    var (kind, limit) = key.Clustered
                        ? ("clustered", ClusteredMaxBytes)
                        : ("nonclustered", nonclusteredMaxBytes);

                    if (fixedBytes > limit)
                    {
                        yield return CreateFinding(statement, DefaultSeverity,
                            $"Key of {key.Name} on {table} is at least {fixedBytes} bytes; the {kind} key limit is " +
                            $"{limit} bytes, so CREATE fails (error 1944).",
                            $"Move wide columns to INCLUDE or shorten them; a {kind} key must stay within {limit} bytes.");
                    }
                    else if (maxBytes > limit)
                    {
                        yield return CreateFinding(statement, Severity.Critical,
                            $"Key of {key.Name} on {table} can reach {maxBytes} bytes; the {kind} key limit is " +
                            $"{limit} bytes — CREATE succeeds with a warning, but any INSERT/UPDATE producing a longer " +
                            "key fails (error 1946).",
                            $"Move wide columns to INCLUDE or shorten them; a {kind} key must stay within {limit} bytes.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Column → data type per table (keys via <see cref="TableNames.Key(SchemaObjectName)"/>),
    /// from every CREATE TABLE and ALTER TABLE … ADD in the file. Computed columns map to
    /// <c>null</c>: their type is not declared.
    /// </summary>
    private static Dictionary<string, Dictionary<string, DataTypeReference?>> CollectColumnTypes(
        IEnumerable<SqlStatementInfo> statements)
    {
        var tables = new Dictionary<string, Dictionary<string, DataTypeReference?>>(StringComparer.Ordinal);

        foreach (var statement in statements)
        {
            var (name, definition) = statement.Ast switch
            {
                CreateTableStatement create => (create.SchemaObjectName, create.Definition),
                AlterTableAddTableElementStatement add => (add.SchemaObjectName, add.Definition),
                _ => (null, null),
            };

            if (TableNames.Key(name) is not { } tableKey || definition is null)
            {
                continue;
            }

            if (!tables.TryGetValue(tableKey, out var columns))
            {
                columns = new Dictionary<string, DataTypeReference?>(StringComparer.OrdinalIgnoreCase);
                tables[tableKey] = columns;
            }

            foreach (var column in definition.ColumnDefinitions)
            {
                if (column.ColumnIdentifier?.Value is { } columnName)
                {
                    columns[columnName] = column.ComputedColumnExpression is null ? column.DataType : null;
                }
            }
        }

        return tables;
    }

    private sealed record KeySpec(string Name, SchemaObjectName? Table, IReadOnlyList<string> Columns, bool Clustered);

    /// <summary>Every index / PRIMARY KEY / UNIQUE key a statement creates, with its key columns.</summary>
    private static IEnumerable<KeySpec> Keys(TSqlStatement statement)
    {
        switch (statement)
        {
            case CreateIndexStatement create:
                yield return new KeySpec(
                    $"index {create.Name?.Value ?? "(unnamed)"}",
                    create.OnName,
                    create.Columns.Select(ColumnName).ToList(),
                    create.Clustered == true);
                break;

            case CreateTableStatement create when create.Definition is { } definition:
                foreach (var key in DefinitionKeys(definition, create.SchemaObjectName))
                {
                    yield return key;
                }

                break;

            case AlterTableAddTableElementStatement add when add.Definition is { } definition:
                foreach (var key in DefinitionKeys(definition, add.SchemaObjectName))
                {
                    yield return key;
                }

                break;
        }
    }

    private static IEnumerable<KeySpec> DefinitionKeys(TableDefinition definition, SchemaObjectName? table)
    {
        foreach (var constraint in definition.TableConstraints.OfType<UniqueConstraintDefinition>())
        {
            if (ConstraintKey(constraint, table, defaultColumn: null) is { } key)
            {
                yield return key;
            }
        }

        foreach (var index in definition.Indexes)
        {
            if (IndexKey(index, table, defaultColumn: null) is { } key)
            {
                yield return key;
            }
        }

        foreach (var column in definition.ColumnDefinitions)
        {
            var columnName = column.ColumnIdentifier?.Value;
            if (columnName is null)
            {
                continue;
            }

            foreach (var constraint in column.Constraints.OfType<UniqueConstraintDefinition>())
            {
                if (ConstraintKey(constraint, table, columnName) is { } key)
                {
                    yield return key;
                }
            }

            if (column.Index is { } inline && IndexKey(inline, table, columnName) is { } inlineKey)
            {
                yield return inlineKey;
            }
        }
    }

    /// <summary>PRIMARY KEY / UNIQUE constraint; hash (memory-optimized) indexes have different limits and are skipped.</summary>
    private static KeySpec? ConstraintKey(UniqueConstraintDefinition constraint, SchemaObjectName? table, string? defaultColumn)
    {
        if (constraint.IndexType?.IndexTypeKind is IndexTypeKind.NonClusteredHash)
        {
            return null;
        }

        var columns = constraint.Columns.Count > 0
            ? constraint.Columns.Select(ColumnName).ToList()
            : defaultColumn is null ? [] : [defaultColumn];

        if (columns.Count == 0)
        {
            return null;
        }

        var kind = constraint.IsPrimaryKey ? "PRIMARY KEY" : "UNIQUE constraint";
        var name = constraint.ConstraintIdentifier?.Value is { } identifier ? $"{kind} {identifier}" : $"the {kind}";
        return new KeySpec(name, table, columns, constraint.Clustered ?? constraint.IsPrimaryKey);
    }

    /// <summary>Inline <c>INDEX</c> definition; columnstore and hash kinds have no key limit of this shape.</summary>
    private static KeySpec? IndexKey(IndexDefinition index, SchemaObjectName? table, string? defaultColumn)
    {
        var kind = index.IndexType?.IndexTypeKind;
        if (kind is IndexTypeKind.NonClusteredHash or IndexTypeKind.ClusteredColumnStore or IndexTypeKind.NonClusteredColumnStore)
        {
            return null;
        }

        var columns = index.Columns.Count > 0
            ? index.Columns.Select(ColumnName).ToList()
            : defaultColumn is null ? [] : [defaultColumn];

        if (columns.Count == 0)
        {
            return null;
        }

        return new KeySpec($"index {index.Name?.Value ?? "(unnamed)"}", table, columns, kind == IndexTypeKind.Clustered);
    }

    private static string ColumnName(ColumnWithSortOrder column)
        => column.Column?.MultiPartIdentifier?.Identifiers is { Count: > 0 } identifiers
            ? identifiers[^1].Value
            : "";
}
