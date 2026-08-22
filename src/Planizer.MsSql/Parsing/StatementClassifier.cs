using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql;

/// <summary>Classifies parsed statements into DDL / DML / DCL / control flow / dynamic SQL (RULES.md section 1).</summary>
public static class StatementClassifier
{
    public static StatementKind Classify(TSqlStatement statement) => statement switch
    {
        ExecuteStatement execute => ClassifyExecute(execute),
        SelectStatement or DataModificationStatement or BulkInsertStatement => StatementKind.Dml,
        _ when IsDcl(statement) => StatementKind.Dcl,
        _ when IsFlow(statement) => StatementKind.Flow,
        _ when IsDdl(statement) => StatementKind.Ddl,
        _ => StatementKind.Other,
    };

    /// <summary>
    /// Bare procedure name (without schema) when the statement is a static <c>EXEC someProc</c> call;
    /// <c>null</c> for anything else (including <c>EXEC @proc</c> and <c>EXEC('…')</c>).
    /// </summary>
    public static string? GetProcedureName(TSqlStatement statement)
        => statement is ExecuteStatement execute
            && execute.ExecuteSpecification?.ExecutableEntity is ExecutableProcedureReference procedure
                ? procedure.ProcedureReference?.ProcedureReference?.Name?.BaseIdentifier?.Value
                : null;

    /// <summary>Whether the statement is a static call to the given (system) procedure, e.g. <c>sp_rename</c>.</summary>
    public static bool IsProcedureCall(TSqlStatement statement, string procedureName)
        => string.Equals(GetProcedureName(statement), procedureName, StringComparison.OrdinalIgnoreCase);

    private static StatementKind ClassifyExecute(ExecuteStatement execute)
    {
        switch (execute.ExecuteSpecification?.ExecutableEntity)
        {
            case ExecutableStringList:
                // EXEC('…') and EXEC(@sql) both surface as a string list.
                return StatementKind.Dynamic;

            case ExecutableProcedureReference procedure:
                if (procedure.ProcedureReference?.ProcedureVariable is not null)
                {
                    // EXEC @procName — variable-based object name, not statically analyzable.
                    return StatementKind.Dynamic;
                }

                if (IsProcedureCall(execute, "sp_executesql"))
                {
                    return StatementKind.Dynamic;
                }

                if (IsProcedureCall(execute, "sp_rename"))
                {
                    // sp_rename is DDL in disguise (rename family).
                    return StatementKind.Ddl;
                }

                return StatementKind.Other;

            default:
                return StatementKind.Other;
        }
    }

    private static bool IsDcl(TSqlStatement statement)
    {
        var typeName = statement.GetType().Name;
        return typeName.StartsWith("Grant", StringComparison.Ordinal)
            || typeName.StartsWith("Deny", StringComparison.Ordinal)
            || typeName.StartsWith("Revoke", StringComparison.Ordinal);
    }

    private static bool IsFlow(TSqlStatement statement)
        => statement is IfStatement
            or WhileStatement
            or BeginEndBlockStatement
            or TryCatchStatement
            or TransactionStatement
            or DeclareVariableStatement
            or PrintStatement
            or ReturnStatement
            or WaitForStatement
            or GoToStatement
            or LabelStatement
            or BreakStatement
            or ContinueStatement
            // Covers the whole SET family (SetCommandStatement, SetIdentityInsertStatement, …).
            || statement.GetType().Name.StartsWith("Set", StringComparison.Ordinal);

    private static bool IsDdl(TSqlStatement statement)
    {
        var typeName = statement.GetType().Name;
        return typeName.StartsWith("Create", StringComparison.Ordinal)
            || typeName.StartsWith("Alter", StringComparison.Ordinal)
            || typeName.StartsWith("Drop", StringComparison.Ordinal)
            || typeName.StartsWith("Truncate", StringComparison.Ordinal)
            || typeName.StartsWith("Rename", StringComparison.Ordinal);
    }
}
