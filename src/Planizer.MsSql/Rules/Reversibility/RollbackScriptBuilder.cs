using Planizer.MsSql.Parsing;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql.Rules.Reversibility;

/// <summary>
/// Generates the inverse statement for reversible DDL, per the plan's inverse pairs:
/// ADD COLUMN→DROP COLUMN, CREATE INDEX→DROP INDEX, ADD CONSTRAINT→DROP CONSTRAINT,
/// CREATE TABLE (and SELECT INTO)→DROP TABLE, sp_rename→reverse sp_rename, CREATE VIEW/PROC→DROP,
/// ENABLE TRIGGER↔DISABLE TRIGGER.
/// Everything else — including CREATE OR ALTER (may replace an existing object) and constraints
/// SQL Server would name itself (the generated name cannot be known statically) — returns
/// <c>null</c>: the analyzer marks the rollback incomplete and MSSQL-REV-002 asks for a manual
/// script where one is feasible.
/// </summary>
public static class RollbackScriptBuilder
{
    /// <summary>
    /// Whether the statement changes database state and therefore needs a rollback entry:
    /// DDL, dynamic SQL (contents unknown, assume the worst) and data-modifying DML.
    /// Pure SELECTs, control flow and SET statements do not count.
    /// </summary>
    public static bool RequiresRollback(SqlStatementInfo statement) => statement.Kind switch
    {
        StatementKind.Ddl => !RestoresSameLogicalState(statement.Ast)
                             && !DmlTargets.TargetsTransientObject(statement.Ast),
        StatementKind.Dynamic => true,
        // Writes to table variables / temp tables move no persistent data — nothing to roll back.
        StatementKind.Dml => DmlTargets.ModifiesPersistentData(statement.Ast),
        _ => false,
    };

    /// <summary>
    /// Index-maintenance statements that leave the schema exactly as it was — REORGANIZE, and a
    /// plain REBUILD without options — have nothing to roll back. A REBUILD WITH (…) changes
    /// persisted index settings (fillfactor, compression, …) and still needs a rollback entry.
    /// </summary>
    private static bool RestoresSameLogicalState(TSqlStatement ast) => ast is AlterIndexStatement alter
        && (alter.AlterIndexType == AlterIndexType.Reorganize
            || (alter.AlterIndexType == AlterIndexType.Rebuild && alter.IndexOptions.Count == 0));

    /// <summary>Inverse SQL for the statement, or <c>null</c> when none can be generated.</summary>
    public static string? TryReverse(SqlStatementInfo statement) => statement.Ast switch
    {
        AlterTableAddTableElementStatement add => ReverseAddElements(add),
        CreateIndexStatement create => ReverseCreateIndex(create),
        CreateTableStatement create => ReverseCreateTable(create),
        SelectStatement { Into: { } into } => $"DROP TABLE {Render(into)};",
        CreateViewStatement view => $"DROP VIEW {Render(view.SchemaObjectName)};",
        CreateProcedureStatement procedure
            => $"DROP PROCEDURE {Render(procedure.ProcedureReference?.Name)};",
        CreateFunctionStatement function => $"DROP FUNCTION {Render(function.Name)};",
        CreateTriggerStatement trigger => ReverseCreateTrigger(trigger),
        // Redefinition of a source-controlled module: the inverse is the previous body, which
        // is not derivable from this script but always exists in version control, and no data
        // is at stake — so the rollback step is a redeploy instruction, not generated SQL.
        CreateOrAlterProcedureStatement p => RedeployPrevious("PROCEDURE", p.ProcedureReference?.Name),
        AlterProcedureStatement p => RedeployPrevious("PROCEDURE", p.ProcedureReference?.Name),
        CreateOrAlterViewStatement v => RedeployPrevious("VIEW", v.SchemaObjectName),
        AlterViewStatement v => RedeployPrevious("VIEW", v.SchemaObjectName),
        CreateOrAlterFunctionStatement f => RedeployPrevious("FUNCTION", f.Name),
        AlterFunctionStatement f => RedeployPrevious("FUNCTION", f.Name),
        CreateOrAlterTriggerStatement t => RedeployPrevious("TRIGGER", t.Name),
        AlterTriggerStatement t => RedeployPrevious("TRIGGER", t.Name),
        ExecuteStatement execute when StatementClassifier.IsProcedureCall(execute, "sp_rename")
            => ReverseSpRename(execute),
        AlterTableTriggerModificationStatement trigger => ReverseTriggerModification(trigger),
        _ => null,
    };

    private static string? ReverseAddElements(AlterTableAddTableElementStatement add)
    {
        if (add.Definition is not { } definition || add.SchemaObjectName is not { } tableName)
        {
            return null;
        }

        if (definition.Indexes is { Count: > 0 })
        {
            return null; // inline index definitions are out of scope; do not guess
        }

        var table = Render(tableName);
        var constraintDrops = new List<string>();
        var columnNames = new List<string>();

        foreach (var column in definition.ColumnDefinitions)
        {
            if (column.ColumnIdentifier?.Value is not { } columnName)
            {
                return null;
            }

            // NOT NULL / NULL is not a droppable constraint; everything else on the column
            // (DEFAULT, CHECK, …) must be dropped before the column can go — and that needs
            // a name. A system-generated name cannot be known statically.
            var droppable = column.Constraints
                .Where(c => c is not NullableConstraintDefinition)
                .Concat(column.DefaultConstraint is { } inlineDefault ? [inlineDefault] : Array.Empty<ConstraintDefinition>())
                .Distinct()
                .ToList();

            foreach (var constraint in droppable)
            {
                if (constraint.ConstraintIdentifier?.Value is not { } constraintName)
                {
                    return null;
                }

                constraintDrops.Add($"ALTER TABLE {table} DROP CONSTRAINT {Quote(constraintName)};");
            }

            columnNames.Add(columnName);
        }

        foreach (var constraint in definition.TableConstraints)
        {
            if (constraint.ConstraintIdentifier?.Value is not { } constraintName)
            {
                return null;
            }

            constraintDrops.Add($"ALTER TABLE {table} DROP CONSTRAINT {Quote(constraintName)};");
        }

        var statements = new List<string>(constraintDrops);
        if (columnNames.Count > 0)
        {
            statements.Add($"ALTER TABLE {table} DROP COLUMN {string.Join(", ", columnNames.Select(Quote))};");
        }

        return statements.Count == 0 ? null : string.Join('\n', statements);
    }

    private static string? ReverseCreateIndex(CreateIndexStatement create)
        => create.Name?.Value is { } indexName && create.OnName is { } table
            ? $"DROP INDEX {Quote(indexName)} ON {Render(table)};"
            : null;

    private static string? RedeployPrevious(string moduleKind, SchemaObjectName? name)
        => name is { Identifiers.Count: > 0 }
            ? $"-- {Render(name)}: redeploy the previous {moduleKind} definition from source control "
              + "(CREATE OR ALTER / ALTER replaced it; the prior body is not derivable offline)."
            : null;

    private static string? ReverseCreateTrigger(CreateTriggerStatement trigger)
    {
        if (trigger.Name is not { Identifiers.Count: > 0 } name)
        {
            return null;
        }

        return trigger.TriggerObject?.TriggerScope switch
        {
            TriggerScope.Database => $"DROP TRIGGER {Render(name)} ON DATABASE;",
            TriggerScope.AllServer => $"DROP TRIGGER {Render(name)} ON ALL SERVER;",
            _ => $"DROP TRIGGER {Render(name)};",
        };
    }

    private static string? ReverseCreateTable(CreateTableStatement create)
        => create.SchemaObjectName is { } name ? $"DROP TABLE {Render(name)};" : null;

    private static string? ReverseSpRename(ExecuteStatement execute)
    {
        if (execute.ExecuteSpecification?.ExecutableEntity is not ExecutableProcedureReference procedure)
        {
            return null;
        }

        // Positional or named (@objname/@newname/@objtype); every used argument must be a
        // string literal, otherwise the rename target is not statically known.
        string? objName = null, newName = null, objType = null;
        for (var i = 0; i < procedure.Parameters.Count; i++)
        {
            var parameter = procedure.Parameters[i];
            if (parameter.ParameterValue is not StringLiteral literal)
            {
                return null;
            }

            var slot = parameter.Variable?.Name?.TrimStart('@').ToLowerInvariant()
                ?? i switch { 0 => "objname", 1 => "newname", 2 => "objtype", _ => "?" };

            switch (slot)
            {
                case "objname": objName = literal.Value; break;
                case "newname": newName = literal.Value; break;
                case "objtype": objType = literal.Value; break;
                default: return null;
            }
        }

        if (objName is null || newName is null)
        {
            return null;
        }

        // 'dbo.Old' + 'New' reverses to 'dbo.New' + 'Old'. @objname is split the way sp_rename
        // reads it (brackets and quotes honoured, e.g. EF Core's N'[T].[Col]'); @newname must be
        // a single bare or bracketed identifier.
        var parts = TableNames.SplitLiteral(objName).ToList();
        var newParts = TableNames.SplitLiteral(newName);
        if (parts.Count == 0 || newParts.Count != 1 || parts.Any(string.IsNullOrEmpty) || newParts[0].Length == 0)
        {
            return null;
        }

        var oldBase = parts[^1];
        parts[^1] = newParts[0];
        var renamedObject = string.Join('.', parts.Select(QuoteIfNeeded));

        var objTypeSuffix = objType is null ? "" : $", '{EscapeLiteral(objType)}'";
        return $"EXEC sp_rename '{EscapeLiteral(renamedObject)}', '{EscapeLiteral(oldBase)}'{objTypeSuffix};";
    }

    /// <summary>ENABLE TRIGGER ↔ DISABLE TRIGGER; the trigger definition itself is untouched.</summary>
    private static string? ReverseTriggerModification(AlterTableTriggerModificationStatement statement)
    {
        if (statement.SchemaObjectName is not { Identifiers.Count: > 0 } table)
        {
            return null;
        }

        var verb = statement.TriggerEnforcement == TriggerEnforcement.Enable ? "DISABLE" : "ENABLE";
        var triggers = statement.All
            ? "ALL"
            : string.Join(", ", statement.TriggerNames.Select(n => Quote(n.Value)));

        return triggers.Length == 0 ? null : $"ALTER TABLE {Render(table)} {verb} TRIGGER {triggers};";
    }

    /// <summary>Regular identifiers stay bare inside the sp_rename literal; anything else is bracketed.</summary>
    private static string QuoteIfNeeded(string identifier)
        => identifier.Length > 0
           && (char.IsLetter(identifier[0]) || identifier[0] is '_' or '@' or '#')
           && identifier.All(c => char.IsLetterOrDigit(c) || c is '_' or '@' or '#' or '$')
            ? identifier
            : Quote(identifier);

    private static string Render(SchemaObjectName? name)
    {
        if (name is null || name.Identifiers.Count == 0)
        {
            return "[?]";
        }

        var builder = new StringBuilder();
        foreach (var identifier in name.Identifiers)
        {
            if (builder.Length > 0)
            {
                builder.Append('.');
            }

            builder.Append(Quote(identifier.Value));
        }

        return builder.ToString();
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    private static string EscapeLiteral(string value) => value.Replace("'", "''");
}
