using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql.Rules.Failure;

/// <summary>
/// MSSQL-BATCH-001: a column added by <c>ALTER TABLE … ADD</c> (or given its new name by
/// <c>sp_rename</c>) in a batch is referenced by a later statement of the same batch. SQL Server
/// compiles a batch as a whole before running any statement in it, so the reference is bound
/// against the table as it exists <em>before</em> the ALTER and fails with error 207
/// (Invalid column name). Only DML and control-flow references count: DDL (indexes,
/// constraints, ALTER COLUMN) binds its columns at execution, dynamic SQL compiles later, and a
/// table created in the same batch gets deferred name resolution for everything that uses it.
/// When the ALTER / rename is itself guarded by a catalog check (<see cref="IdempotencyGuard"/>)
/// the message says that the failure is environment-dependent: the batch only compiles where an
/// earlier run already added the column, which is how such scripts survive on incrementally
/// deployed environments and then fail on a fresh one.
/// </summary>
public sealed class NewColumnUsedInSameBatchRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-BATCH-001";
    public override string Title => "Column added in the same batch is referenced before GO";
    public override Severity DefaultSeverity => Severity.Blocker;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var batch in context.Batches)
        {
            var newColumns = new List<NewColumn>();
            var createdTables = new HashSet<string>(StringComparer.Ordinal);

            foreach (var statement in context.StatementsInBatch(batch.Index))
            {
                if (newColumns.Count > 0
                    && statement.Kind != StatementKind.Ddl
                    && !StatementScan.IsModuleDefinition(statement.Ast)
                    && FindReferences(statement, newColumns) is { Count: > 0 } referenced)
                {
                    yield return Report(statement, referenced, context);
                }

                Register(statement, newColumns, createdTables);
            }
        }
    }

    private Finding Report(SqlStatementInfo statement, IReadOnlyList<NewColumn> referenced, MsSqlAnalysisContext context)
    {
        var columns = string.Join(", ", referenced.Select(c =>
            $"{c.TableDisplay}.{c.Column} ({c.Verb} at line {c.Line})"));
        var plural = referenced.Count > 1;
        var lastOrigin = referenced.Max(c => c.Line);

        var guarded = referenced.Where(c => IdempotencyGuard.IsGuarded(c.Origin, context)).ToList();
        var environment = guarded.Count == 0
            ? ""
            : $" The statement{(guarded.Count == 1 ? "" : "s")} introducing {(guarded.Count == 1 ? "it" : "them")} " +
              $"({string.Join(", ", guarded.Select(c => $"line {c.Line}"))}) " +
              $"{(guarded.Count == 1 ? "is" : "are")} guarded by a catalog check, so the batch fails on any database " +
              "where the column does not exist yet (a fresh environment or a first deployment); it only compiles " +
              "where an earlier run already added the column.";

        return CreateFinding(statement, Severity.Blocker,
            $"This statement references {(plural ? "columns" : "column")} {columns} in the same batch; " +
            "a batch is compiled as a whole before any statement in it runs, so the " +
            $"{(plural ? "columns do" : "column does")} not exist yet at compile time and the batch fails " +
            $"with error 207 (Invalid column name '{referenced[0].Column}').{environment}",
            fix: $"Put GO after line {lastOrigin} so this statement compiles in a later batch; where GO is " +
                 "not available, run it as dynamic SQL so it compiles after the ALTER: " +
                 $"EXEC sp_executesql N'{Escape(DescribeStatement(statement, 200))}';");
    }

    private static string Escape(string sql) => sql.Replace("'", "''");

    /// <summary>Remembers columns this statement introduces and tables it creates.</summary>
    private static void Register(SqlStatementInfo statement, List<NewColumn> newColumns, HashSet<string> createdTables)
    {
        switch (statement.Ast)
        {
            case CreateTableStatement create when TableNames.Key(create.SchemaObjectName) is { } key:
                createdTables.Add(key);
                break;

            case SelectStatement { Into: { } into } when TableNames.Key(into) is { } key:
                createdTables.Add(key);
                break;

            case AlterTableAddTableElementStatement add
                when add.SchemaObjectName is { Identifiers.Count: > 0 } table
                     && TableNames.Key(table) is { } key
                     && !createdTables.Contains(key):
                foreach (var column in add.Definition?.ColumnDefinitions ?? [])
                {
                    if (column.ColumnIdentifier?.Value is { Length: > 0 } name)
                    {
                        newColumns.Add(new NewColumn(key, table.BaseIdentifier.Value,
                            TableNames.Display(TableNames.Parts(table)), name, statement, "added"));
                    }
                }

                break;

            default:
                if (StatementScan.SpRenameArguments(statement.Ast) is var (objName, newName, objType)
                    && string.Equals(objType?.Trim(), "COLUMN", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = TableNames.SplitLiteral(objName);
                    var newParts = TableNames.SplitLiteral(newName);
                    if (parts.Count < 2 || newParts.Count != 1 || newParts[0].Length == 0 || parts.Any(string.IsNullOrEmpty))
                    {
                        break;
                    }

                    var tableParts = parts.Take(parts.Count - 1).ToList();
                    var renamedKey = TableNames.Key(tableParts);
                    if (!createdTables.Contains(renamedKey))
                    {
                        newColumns.Add(new NewColumn(renamedKey, tableParts[^1], TableNames.Display(tableParts),
                            newParts[0], statement, "renamed"));
                    }
                }

                break;
        }
    }

    /// <summary>The new columns this statement references, in registration order.</summary>
    private static List<NewColumn> FindReferences(SqlStatementInfo statement, List<NewColumn> newColumns)
    {
        var collector = new ReferenceCollector();
        foreach (var fragment in StatementScan.OwnFragments(statement))
        {
            fragment.Accept(collector);
        }

        if (collector.Columns.Count == 0)
        {
            return [];
        }

        return newColumns.Where(column => References(collector, column)).ToList();
    }

    /// <summary>
    /// Matching: the statement must name the column's table somewhere, the column name must match,
    /// and a qualifier (if any) must be the table, an alias of the table, or unresolvable. An
    /// alias of a different table, another table's name, or the alias of a derived table / CTE /
    /// table variable / table-valued function (a column of <em>that</em> row source) is not a
    /// reference to this column.
    /// </summary>
    private static bool References(ReferenceCollector refs, NewColumn column)
    {
        if (!refs.Tables.Contains(column.TableKey))
        {
            return false;
        }

        foreach (var reference in refs.Columns)
        {
            var identifiers = reference.MultiPartIdentifier.Identifiers;
            if (!Same(identifiers[^1].Value, column.Column))
            {
                continue;
            }

            if (identifiers.Count == 1)
            {
                return true; // unqualified
            }

            if (identifiers.Count >= 3)
            {
                if (TableNames.Key([identifiers[^3].Value, identifiers[^2].Value]) == column.TableKey)
                {
                    return true;
                }

                continue;
            }

            var qualifier = identifiers[0].Value;
            if (Same(qualifier, column.TableBase))
            {
                return true;
            }

            if (refs.Aliases.TryGetValue(qualifier, out var aliasedTables))
            {
                if (aliasedTables.Contains(column.TableKey))
                {
                    return true;
                }

                continue; // alias of another table
            }

            if (refs.TableBaseNames.Contains(qualifier) || refs.OtherQualifiers.Contains(qualifier))
            {
                continue; // another table's name, or a derived table / CTE / variable / function alias
            }

            return true; // unknown qualifier (e.g. an alias from an outer scope): conservative
        }

        return false;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>A column introduced in the batch: by which statement (<see cref="Origin"/>) and how (<see cref="Verb"/>: added / renamed).</summary>
    private sealed record NewColumn(
        string TableKey,
        string TableBase,
        string TableDisplay,
        string Column,
        SqlStatementInfo Origin,
        string Verb)
    {
        public int Line => Origin.Location.Line;
    }

    private sealed class ReferenceCollector : TSqlFragmentVisitor
    {
        public List<ColumnReferenceExpression> Columns { get; } = [];
        public HashSet<string> Tables { get; } = new(StringComparer.Ordinal);
        public HashSet<string> TableBaseNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HashSet<string>> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Aliases of row sources that are not persistent tables: derived tables, CTEs, table variables, TVFs, OPENJSON, PIVOT…</summary>
        public HashSet<string> OtherQualifiers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void Visit(ColumnReferenceExpression node)
        {
            if (node.MultiPartIdentifier is { Count: > 0 })
            {
                Columns.Add(node);
            }
        }

        public override void Visit(NamedTableReference node)
        {
            if (TableNames.Key(node.SchemaObject) is not { } key)
            {
                return;
            }

            Tables.Add(key);
            TableBaseNames.Add(node.SchemaObject.BaseIdentifier.Value);

            if (node.Alias?.Value is { Length: > 0 } alias)
            {
                if (!Aliases.TryGetValue(alias, out var keys))
                {
                    Aliases[alias] = keys = new HashSet<string>(StringComparer.Ordinal);
                }

                keys.Add(key);
            }
        }

        /// <summary>Every aliased row source other than a named table (those take the override above).</summary>
        public override void Visit(TableReferenceWithAlias node)
        {
            if (node is not NamedTableReference && node.Alias?.Value is { Length: > 0 } alias)
            {
                OtherQualifiers.Add(alias);
            }
        }

        public override void Visit(CommonTableExpression node)
        {
            if (node.ExpressionName?.Value is { Length: > 0 } name)
            {
                OtherQualifiers.Add(name);
            }
        }
    }
}
