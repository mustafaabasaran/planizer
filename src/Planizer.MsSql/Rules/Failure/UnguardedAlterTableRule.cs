using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql.Rules.Failure;

/// <summary>
/// MSSQL-IDEM-002: ALTER TABLE … ADD COLUMN / ADD CONSTRAINT / DROP COLUMN / DROP CONSTRAINT
/// without an existence check. A second run fails with "Column names in each table must be
/// unique" (2705), "There is already an object named …" (2714), "column … does not exist" (4924)
/// or "… is not a constraint" (3728). An <em>unnamed</em> CHECK / FOREIGN KEY / UNIQUE does not
/// fail on the second run — SQL Server generates a fresh name and silently adds a duplicate
/// constraint; only an unnamed PRIMARY KEY (1779) or DEFAULT (1781) fails. Element-level
/// <c>IF EXISTS</c> (2016+), a catalog-querying enclosing IF or exit guard, a safe DROP of the
/// same element earlier in the file (for ADD) or an ADD of the same element earlier in the file
/// (for DROP — the helper-column pattern) all count as guards.
/// </summary>
public sealed class UnguardedAlterTableRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-IDEM-002";
    public override string Title => "ALTER TABLE ADD/DROP without an existence check is not re-runnable";
    public override Severity DefaultSeverity => Severity.Warning;

    private enum Change { AddColumn, AddConstraint, DropColumn, DropConstraint }

    private enum ConstraintKind { None, Check, ForeignKey, Unique, PrimaryKey, Default }

    /// <param name="Kind">Constraint flavour of an ADD CONSTRAINT; decides what an unnamed re-run does.</param>
    /// <param name="Column">Target column of an unnamed DEFAULT (<c>ADD DEFAULT 0 FOR X</c>).</param>
    private sealed record Element(Change Change, string? Name, ConstraintKind Kind = ConstraintKind.None, string? Column = null)
    {
        public bool IsAdd => Change is Change.AddColumn or Change.AddConstraint;
        public bool IsColumn => Change is Change.AddColumn or Change.DropColumn;

        public string Describe()
            => (IsColumn ? "column " : "constraint ") + (Name ?? "(unnamed)");

        public string KindWord => Kind switch
        {
            ConstraintKind.Check => "CHECK",
            ConstraintKind.ForeignKey => "FOREIGN KEY",
            ConstraintKind.Unique => "UNIQUE",
            ConstraintKind.PrimaryKey => "PRIMARY KEY",
            ConstraintKind.Default => "DEFAULT",
            _ => "",
        };
    }

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (TableOf(statement.Ast) is not { } table
                || DmlTargets.IsTransient(table)
                || IdempotencyGuard.IsGuarded(statement, context))
            {
                continue;
            }

            var unguarded = Elements(statement.Ast)
                .Where(e => !PairedEarlierInFile(statement, context, table, e))
                .ToList();

            if (unguarded.Count == 0)
            {
                continue;
            }

            var verb = unguarded[0].IsAdd ? "ADD" : "DROP";
            var tableName = IdempotencyTargets.Display(table);
            var elements = string.Join(", ", unguarded.Select(e => e.Describe()));

            yield return CreateFinding(statement, DefaultSeverity,
                $"ALTER TABLE {tableName} {verb} {elements} is not guarded by an existence check; " +
                $"{Consequence(unguarded)}.",
                Fix(statement, tableName, unguarded[0], context.Config.TargetVersion));
        }
    }

    private static SchemaObjectName? TableOf(TSqlStatement statement) => statement switch
    {
        AlterTableAddTableElementStatement add => add.SchemaObjectName,
        AlterTableDropTableElementStatement drop => drop.SchemaObjectName,
        _ => null,
    };

    /// <summary>Columns and constraints the statement adds or drops; element-level IF EXISTS drops are already safe.</summary>
    private static IEnumerable<Element> Elements(TSqlStatement statement)
    {
        switch (statement)
        {
            case AlterTableAddTableElementStatement { Definition: { } definition }:
                foreach (var column in definition.ColumnDefinitions)
                {
                    yield return new Element(Change.AddColumn, column.ColumnIdentifier?.Value);
                }

                foreach (var constraint in definition.TableConstraints)
                {
                    yield return new Element(
                        Change.AddConstraint,
                        constraint.ConstraintIdentifier?.Value,
                        KindOf(constraint),
                        (constraint as DefaultConstraintDefinition)?.Column?.Value);
                }

                break;

            case AlterTableDropTableElementStatement drop:
                foreach (var element in drop.AlterTableDropTableElements.Where(e => !e.IsIfExists))
                {
                    switch (element.TableElementType)
                    {
                        case TableElementType.Column:
                            yield return new Element(Change.DropColumn, element.Name?.Value);
                            break;
                        case TableElementType.Constraint:
                        case TableElementType.NotSpecified: // ALTER TABLE T DROP CK_Name — constraint by default
                            yield return new Element(Change.DropConstraint, element.Name?.Value);
                            break;
                    }
                }

                break;
        }
    }

    /// <summary>
    /// ADD is re-runnable when the file safely dropped the same element earlier; DROP is
    /// re-runnable when the file itself added the element earlier (a re-run adds it again first).
    /// </summary>
    private static bool PairedEarlierInFile(
        SqlStatementInfo statement, MsSqlAnalysisContext context, SchemaObjectName table, Element element)
    {
        if (element.Name is null)
        {
            return false;
        }

        foreach (var earlier in IdempotencyTargets.EarlierInFile(statement, context))
        {
            if (TableOf(earlier.Ast) is not { } earlierTable || !IdempotencyTargets.SameName(table, earlierTable))
            {
                continue;
            }

            if (element.IsAdd)
            {
                if (earlier.Ast is AlterTableDropTableElementStatement drop
                    && drop.AlterTableDropTableElements.Any(e =>
                        SameElement(e, element) && (e.IsIfExists || IdempotencyGuard.IsGuarded(earlier, context))))
                {
                    return true;
                }
            }
            else if (earlier.Ast is AlterTableAddTableElementStatement { Definition: { } added }
                     && (element.IsColumn
                         ? added.ColumnDefinitions.Any(c => Same(c.ColumnIdentifier, element.Name))
                         : added.TableConstraints.Any(c => Same(c.ConstraintIdentifier, element.Name))
                           || added.ColumnDefinitions.SelectMany(c => c.Constraints).Any(c => Same(c.ConstraintIdentifier, element.Name))))
            {
                return true;
            }
        }

        return false;
    }

    private static ConstraintKind KindOf(ConstraintDefinition constraint) => constraint switch
    {
        CheckConstraintDefinition => ConstraintKind.Check,
        ForeignKeyConstraintDefinition => ConstraintKind.ForeignKey,
        UniqueConstraintDefinition { IsPrimaryKey: true } => ConstraintKind.PrimaryKey,
        UniqueConstraintDefinition => ConstraintKind.Unique,
        DefaultConstraintDefinition => ConstraintKind.Default,
        _ => ConstraintKind.None,
    };

    private static bool SameElement(AlterTableDropTableElement dropped, Element element)
        => Same(dropped.Name, element.Name)
           && (element.IsColumn
               ? dropped.TableElementType == TableElementType.Column
               : dropped.TableElementType is TableElementType.Constraint or TableElementType.NotSpecified);

    private static bool Same(Identifier? identifier, string? name)
        => identifier?.Value is { } value && name is not null
           && string.Equals(value, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>What the second run does, as a full clause ("running the script a second time …").</summary>
    private static string Consequence(IReadOnlyList<Element> elements)
    {
        var first = elements[0];
        var plural = elements.Count > 1;
        const string fails = "running the script a second time fails because ";

        return first.Change switch
        {
            Change.AddColumn => fails + (plural ? "the columns already exist (error 2705)" : "the column already exists (error 2705)"),
            Change.AddConstraint when first.Name is not null => fails + (plural
                ? "constraints with these names already exist (error 2714)"
                : $"a constraint named {first.Name} already exists (error 2714)"),
            Change.AddConstraint => first.Kind switch
            {
                ConstraintKind.PrimaryKey => fails + "the table already has a primary key (error 1779)",
                ConstraintKind.Default => fails + "the column already has a DEFAULT constraint (error 1781)",
                ConstraintKind.Check or ConstraintKind.ForeignKey or ConstraintKind.Unique =>
                    "running the script a second time does not fail but adds a second, duplicate " +
                    $"{first.KindWord} constraint under a new system-generated name",
                _ => fails + "the constraint already exists",
            },
            Change.DropColumn => fails + (plural ? "the columns no longer exist (error 4924)" : "the column no longer exists (error 4924)"),
            _ => fails + (plural ? "the constraints no longer exist (error 3728)" : "the constraint no longer exists (error 3728)"),
        };
    }

    private static string Fix(SqlStatementInfo statement, string table, Element element, SqlServerVersion target)
    {
        var sql = IdempotencyTargets.Collapse(statement.Sql);

        switch (element.Change)
        {
            case Change.AddColumn:
                return $"Guard it: IF COL_LENGTH(N'{table}', N'{element.Name}') IS NULL {sql}";

            case Change.AddConstraint when element is { Name: null, Kind: ConstraintKind.PrimaryKey }:
                return "Name the constraint and guard it: " +
                       $"IF OBJECTPROPERTY(OBJECT_ID(N'{table}'), 'TableHasPrimaryKey') = 0 {sql}";

            case Change.AddConstraint when element is { Name: null, Kind: ConstraintKind.Default, Column: { } column }:
                return "Name the constraint and guard it: IF NOT EXISTS (SELECT 1 FROM sys.default_constraints " +
                       $"WHERE parent_object_id = OBJECT_ID(N'{table}') " +
                       $"AND parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'{table}'), N'{column}', 'ColumnId')) {sql}";

            case Change.AddConstraint when element.Name is null:
                return "Name the constraint and guard it: IF NOT EXISTS (SELECT 1 FROM sys.objects " +
                       $"WHERE name = N'<constraint>' AND parent_object_id = OBJECT_ID(N'{table}')) {sql}";

            case Change.AddConstraint:
                return $"Guard it: IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = N'{element.Name}' " +
                       $"AND parent_object_id = OBJECT_ID(N'{table}')) {sql}";

            case Change.DropColumn when IdempotencyTargets.SupportsDropIfExists(target):
                return $"Use ALTER TABLE {table} DROP COLUMN IF EXISTS {element.Name};";

            case Change.DropColumn:
                return $"Guard it: IF COL_LENGTH(N'{table}', N'{element.Name}') IS NOT NULL {sql}";

            case Change.DropConstraint when IdempotencyTargets.SupportsDropIfExists(target):
                return $"Use ALTER TABLE {table} DROP CONSTRAINT IF EXISTS {element.Name};";

            default:
                return $"Guard it: IF EXISTS (SELECT 1 FROM sys.objects WHERE name = N'{element.Name}' " +
                       $"AND parent_object_id = OBJECT_ID(N'{table}')) {sql}";
        }
    }
}
