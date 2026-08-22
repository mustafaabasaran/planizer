using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql.Rules.Failure;

/// <summary>A persistent object a CREATE statement introduces — or a DROP removes — as the IDEM rules see it.</summary>
/// <param name="Kind">Lower-case noun for messages: "table", "index", "procedure", …</param>
/// <param name="Keyword">Statement head for messages: "CREATE TABLE", "DROP INDEX", …</param>
/// <param name="Name">Object name; for an index the bare index name.</param>
/// <param name="Parent">Owning table of an index; <c>null</c> otherwise.</param>
/// <param name="ObjectType">The <c>OBJECT_ID(name, type)</c> type code when one applies; <c>null</c> for index / type / schema / DDL trigger / function.</param>
/// <param name="IsModule">Procedure, function, trigger or view — the objects <c>CREATE OR ALTER</c> accepts.</param>
/// <param name="TriggerScope">Scope of a trigger; <c>Normal</c> for everything else.</param>
internal sealed record SchemaObject(
    string Kind,
    string Keyword,
    SchemaObjectName Name,
    SchemaObjectName? Parent,
    string? ObjectType,
    bool IsModule,
    TriggerScope TriggerScope = TriggerScope.Normal)
{
    public string Display => IdempotencyTargets.Display(Name);

    /// <summary>Boolean T-SQL that is true when the object exists, e.g. <c>OBJECT_ID(N'dbo.T', N'U') IS NOT NULL</c>.</summary>
    public string ExistsPredicate() => Predicate(exists: true);

    /// <summary>Boolean T-SQL that is true when the object is absent, e.g. <c>OBJECT_ID(N'dbo.T', N'U') IS NULL</c>.</summary>
    public string MissingPredicate() => Predicate(exists: false);

    private string Predicate(bool exists)
    {
        var isNull = exists ? "IS NOT NULL" : "IS NULL";
        var existsWord = exists ? "EXISTS" : "NOT EXISTS";

        return Kind switch
        {
            "index" or "columnstore index" =>
                $"{existsWord} (SELECT 1 FROM sys.indexes WHERE name = N'{Name.BaseIdentifier.Value}' AND object_id = OBJECT_ID(N'{IdempotencyTargets.Display(Parent)}'))",
            "type" => $"TYPE_ID(N'{Display}') {isNull}",
            "schema" => $"SCHEMA_ID(N'{Display}') {isNull}",
            "trigger" when TriggerScope == TriggerScope.AllServer =>
                $"{existsWord} (SELECT 1 FROM sys.server_triggers WHERE name = N'{Name.BaseIdentifier.Value}')",
            "trigger" when TriggerScope == TriggerScope.Database =>
                $"{existsWord} (SELECT 1 FROM sys.triggers WHERE parent_class = 0 AND name = N'{Name.BaseIdentifier.Value}')",
            _ when ObjectType is null => $"OBJECT_ID(N'{Display}') {isNull}",
            _ => $"OBJECT_ID(N'{Display}', N'{ObjectType}') {isNull}",
        };
    }

    /// <summary>Same kind of object with the same name (and, for indexes, the same table when both name it).</summary>
    public bool SameAs(SchemaObject other)
        => string.Equals(Kind, other.Kind, StringComparison.Ordinal)
           && IdempotencyTargets.SameName(Name, other.Name)
           && (Parent is null || other.Parent is null || IdempotencyTargets.SameName(Parent, other.Parent));
}

/// <summary>
/// Shared plumbing of the MSSQL-IDEM family: which CREATE / DROP statements name a persistent
/// object, how to spell the object in messages and guards, and the "did this file already
/// create / drop it" look-backs.
/// </summary>
internal static class IdempotencyTargets
{
    /// <summary>Dotted display form of a name; "the object" when absent.</summary>
    public static string Display(SchemaObjectName? name)
        => name is null || name.Identifiers.Count == 0
            ? "the object"
            : string.Join(".", name.Identifiers.Select(i => i.Value));

    /// <summary>Case-insensitive, quote-insensitive name equality; an unqualified name is assumed to be in <c>dbo</c>.</summary>
    public static bool SameName(SchemaObjectName? a, SchemaObjectName? b)
        => TableNames.Key(a) is { } left && TableNames.Key(b) is { } right
           && string.Equals(left, right, StringComparison.Ordinal);

    public static bool SameIdentifier(Identifier? a, Identifier? b)
        => a?.Value is { } left && b?.Value is { } right
           && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>Wraps a bare identifier (index name, schema name) as a one-part <see cref="SchemaObjectName"/>.</summary>
    public static SchemaObjectName NameOf(Identifier identifier)
    {
        var name = new SchemaObjectName();
        name.Identifiers.Add(identifier);
        return name;
    }

    /// <summary>The statement text on one line, whitespace collapsed — for "fixed SQL" suggestions.</summary>
    public static string Collapse(string sql)
        => string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Whether <c>CREATE OR ALTER</c> is available on the target (2016 SP1 and later; plain 2016 is treated as "maybe").</summary>
    public static bool SupportsCreateOrAlter(SqlServerVersion version) => version > SqlServerVersion.Sql2016;

    /// <summary>Whether <c>DROP … IF EXISTS</c> is available on the target (2016 and later).</summary>
    public static bool SupportsDropIfExists(SqlServerVersion version) => version >= SqlServerVersion.Sql2016;

    /// <summary>Statements of the same file that run before <paramref name="statement"/>.</summary>
    public static IEnumerable<SqlStatementInfo> EarlierInFile(SqlStatementInfo statement, MsSqlAnalysisContext context)
        => context.StatementsInFile(statement.Location.File).TakeWhile(s => s.Index < statement.Index);

    /// <summary>
    /// The persistent object a CREATE statement introduces; <c>null</c> for statements outside the
    /// IDEM scope (temp tables, CTAS, unnamed schemas, everything that is not a CREATE). The
    /// <c>isCreateOrAlter</c> flag lets callers treat <c>CREATE OR ALTER</c> as already idempotent
    /// while still counting it as "created earlier in this file".
    /// </summary>
    public static (SchemaObject Object, bool IsCreateOrAlter)? Created(TSqlStatement statement)
    {
        switch (statement)
        {
            case CreateTableStatement { SchemaObjectName: { } table, SelectStatement: null }
                when !DmlTargets.IsTransient(table):
                return (new SchemaObject("table", "CREATE TABLE", table, null, "U", IsModule: false), false);

            case CreateIndexStatement { Name: { } index, OnName: { } on } when !DmlTargets.IsTransient(on):
                return (new SchemaObject("index", "CREATE INDEX", NameOf(index), on, null, IsModule: false), false);

            case CreateColumnStoreIndexStatement { Name: { } index, OnName: { } on } when !DmlTargets.IsTransient(on):
                return (new SchemaObject("columnstore index", "CREATE COLUMNSTORE INDEX", NameOf(index), on, null, IsModule: false), false);

            case CreateOrAlterViewStatement { SchemaObjectName: { } view }:
                return (new SchemaObject("view", "CREATE VIEW", view, null, "V", IsModule: true), true);
            case CreateViewStatement { SchemaObjectName: { } view }:
                return (new SchemaObject("view", "CREATE VIEW", view, null, "V", IsModule: true), false);

            case CreateOrAlterProcedureStatement { ProcedureReference.Name: { } proc }:
                return (new SchemaObject("procedure", "CREATE PROCEDURE", proc, null, "P", IsModule: true), true);
            case CreateProcedureStatement { ProcedureReference.Name: { } proc }:
                return (new SchemaObject("procedure", "CREATE PROCEDURE", proc, null, "P", IsModule: true), false);

            case CreateOrAlterFunctionStatement { Name: { } function }:
                return (new SchemaObject("function", "CREATE FUNCTION", function, null, null, IsModule: true), true);
            case CreateFunctionStatement { Name: { } function }:
                return (new SchemaObject("function", "CREATE FUNCTION", function, null, null, IsModule: true), false);

            case CreateOrAlterTriggerStatement { Name: { } trigger } t:
                return (Trigger("CREATE TRIGGER", trigger, t.TriggerObject?.TriggerScope ?? TriggerScope.Normal), true);
            case CreateTriggerStatement { Name: { } trigger } t:
                return (Trigger("CREATE TRIGGER", trigger, t.TriggerObject?.TriggerScope ?? TriggerScope.Normal), false);

            case CreateTypeStatement { Name: { } type }:
                return (new SchemaObject("type", "CREATE TYPE", type, null, null, IsModule: false), false);

            case CreateSchemaStatement { Name: { } schema }:
                return (new SchemaObject("schema", "CREATE SCHEMA", NameOf(schema), null, null, IsModule: false), false);

            case CreateSequenceStatement { Name: { } sequence }:
                return (new SchemaObject("sequence", "CREATE SEQUENCE", sequence, null, "SO", IsModule: false), false);

            default:
                return null;
        }
    }

    /// <summary>
    /// The persistent table a <c>SELECT … INTO</c> creates; <c>null</c> for temp tables and other
    /// statements. Deliberately not part of <see cref="Created"/>: MSSQL-IDEM-001 leaves
    /// <c>SELECT … INTO</c> alone, but a later <c>DROP</c> of that table is still the staging
    /// pattern MSSQL-IDEM-003 exempts.
    /// </summary>
    public static SchemaObject? SelectedInto(TSqlStatement statement)
        => statement is SelectStatement { Into: { } into } && !DmlTargets.IsTransient(into)
            ? new SchemaObject("table", "SELECT INTO", into, null, "U", IsModule: false)
            : null;

    /// <summary>
    /// The persistent objects a DROP statement removes, each with whether its own syntax already
    /// tolerates absence (<c>IF EXISTS</c>). Empty for temp tables and statements outside scope.
    /// </summary>
    public static IReadOnlyList<(SchemaObject Object, bool IsIfExists)> Dropped(TSqlStatement statement)
    {
        switch (statement)
        {
            case DropTableStatement drop:
                return Objects(drop, "table", "DROP TABLE", "U");
            case DropViewStatement drop:
                return Objects(drop, "view", "DROP VIEW", "V");
            case DropProcedureStatement drop:
                return Objects(drop, "procedure", "DROP PROCEDURE", "P");
            case DropFunctionStatement drop:
                return Objects(drop, "function", "DROP FUNCTION", null);
            case DropSequenceStatement drop:
                return Objects(drop, "sequence", "DROP SEQUENCE", "SO");

            case DropTriggerStatement drop:
                return drop.Objects
                    .Select(name => (Trigger("DROP TRIGGER", name, drop.TriggerScope), drop.IsIfExists))
                    .ToList();

            case DropTypeStatement { Name: { } type } drop:
                return [(new SchemaObject("type", "DROP TYPE", type, null, null, IsModule: false), drop.IsIfExists)];

            case DropSchemaStatement { Schema: { } schema } drop:
                return [(new SchemaObject("schema", "DROP SCHEMA", schema, null, null, IsModule: false), drop.IsIfExists)];

            case DropIndexStatement drop:
                return drop.DropIndexClauses
                    .Select(clause => clause switch
                    {
                        DropIndexClause { Index: { } index, Object: { } on } when !DmlTargets.IsTransient(on)
                            => new SchemaObject("index", "DROP INDEX", NameOf(index), on, null, IsModule: false),
                        // Legacy "DROP INDEX table.index": ChildIdentifier is the index, the rest is the table.
                        BackwardsCompatibleDropIndexClause { Index: { ChildIdentifier: { } index } child }
                            when child.Identifiers.Count >= 2 && !DmlTargets.IsTransient(TableOf(child))
                            => new SchemaObject("index", "DROP INDEX", NameOf(index), TableOf(child), null, IsModule: false),
                        _ => null,
                    })
                    .Where(o => o is not null)
                    .Select(o => (o!, drop.IsIfExists))
                    .ToList();

            default:
                return [];
        }
    }

    private static SchemaObject Trigger(string keyword, SchemaObjectName name, TriggerScope scope)
        => new("trigger", keyword, name, null, scope == TriggerScope.Normal ? "TR" : null, IsModule: true, scope);

    private static IReadOnlyList<(SchemaObject, bool)> Objects(
        DropObjectsStatement drop, string kind, string keyword, string? objectType)
        => drop.Objects
            .Where(name => !DmlTargets.IsTransient(name))
            .Select(name => (new SchemaObject(kind, keyword, name, null, objectType, IsModule: false), drop.IsIfExists))
            .ToList();

    /// <summary>The table part of the legacy <c>DROP INDEX table.index</c> spelling.</summary>
    private static SchemaObjectName TableOf(ChildObjectName child)
    {
        var table = new SchemaObjectName();
        foreach (var identifier in child.Identifiers.Take(child.Identifiers.Count - 1))
        {
            table.Identifiers.Add(identifier);
        }

        return table;
    }
}
