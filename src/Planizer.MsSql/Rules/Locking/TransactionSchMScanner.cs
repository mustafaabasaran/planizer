using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql.Rules.Locking;

/// <summary>
/// A table a Sch-M statement locks. <see cref="Key"/> identifies the table regardless of how the
/// script spells it (quoting, case, implicit dbo schema); <see cref="Display"/> is what the finding
/// prints.
/// </summary>
internal sealed record LockedTable(IReadOnlyList<string> Parts)
{
    public string Display => TableNames.Display(Parts);
    public string Key => TableNames.Key(Parts);
}

/// <summary>
/// Shared helper for the transaction-scoped locking rules (LOCK-007/008): finds the statements
/// inside an explicit transaction that acquire a schema-modification (Sch-M) lock, and names the
/// tables they lock.
/// </summary>
internal static class TransactionSchMScanner
{
    /// <summary>
    /// The Sch-M-acquiring statements enclosed in <paramref name="scope"/>, in script order.
    /// </summary>
    public static IReadOnlyList<SqlStatementInfo> SchMStatements(
        MsSqlAnalysisContext context, TransactionScope scope)
        => scope.StatementIndices
            .Select(context.StatementAt)
            .Where(s => DdlOperationClassifier.AcquiresSchMLock(s, context.Catalog, context.Config))
            .ToList();

    /// <summary>
    /// The tables a Sch-M statement locks; empty when the AST does not name one.
    /// Multiple tables are possible (<c>DROP TABLE a, b</c>).
    /// </summary>
    public static IReadOnlyList<LockedTable> LockedTables(SqlStatementInfo statement) => statement.Ast switch
    {
        AlterTableStatement alter => Tables(alter.SchemaObjectName),
        DropTableStatement drop => drop.Objects.SelectMany(Tables).ToList(),
        TruncateTableStatement truncate => Tables(truncate.TableName),
        IndexStatement index => Tables(index.OnName),
        _ when StatementClassifier.IsProcedureCall(statement.Ast, "sp_rename")
            => SpRenameLockTargetParts(statement.Ast) is { } renamed ? [new LockedTable(renamed)] : [],
        _ => [],
    };

    /// <summary>
    /// Display name of the object an <c>sp_rename</c> call takes its Sch-M lock on (quoting
    /// removed: <c>[dbo].[T]</c> → <c>dbo.T</c>); <c>null</c> when the name is not a string
    /// literal (not statically known).
    /// </summary>
    internal static string? SpRenameLockTarget(TSqlStatement ast)
        => SpRenameLockTargetParts(ast) is { } parts ? TableNames.Display(parts) : null;

    /// <summary>
    /// The object an <c>sp_rename</c> call takes its Sch-M lock on, as unquoted name parts. For
    /// COLUMN / INDEX / STATISTICS renames the last part of <c>@objname</c> names the sub-object,
    /// so the lock target is the table before it; every other object type is locked under its
    /// full name.
    /// </summary>
    private static IReadOnlyList<string>? SpRenameLockTargetParts(TSqlStatement ast)
    {
        if (ast is not ExecuteStatement
            {
                ExecuteSpecification.ExecutableEntity: ExecutableProcedureReference procedure,
            })
        {
            return null;
        }

        // Positional or named (@objname/@objtype); ignore arguments that are not string literals.
        string? objName = null, objType = null;
        for (var i = 0; i < procedure.Parameters.Count; i++)
        {
            var parameter = procedure.Parameters[i];
            if (parameter.ParameterValue is not StringLiteral literal)
            {
                continue;
            }

            var slot = parameter.Variable?.Name?.TrimStart('@').ToLowerInvariant()
                ?? i switch { 0 => "objname", 2 => "objtype", _ => "?" };

            switch (slot)
            {
                case "objname": objName ??= literal.Value; break;
                case "objtype": objType ??= literal.Value; break;
            }
        }

        if (objName is null)
        {
            return null;
        }

        var parts = TableNames.SplitLiteral(objName);
        var isSubObject = objType?.Trim().ToUpperInvariant() is "COLUMN" or "INDEX" or "STATISTICS";
        return isSubObject && parts.Count > 1 ? parts.Take(parts.Count - 1).ToList() : parts;
    }

    private static IReadOnlyList<LockedTable> Tables(SchemaObjectName? name)
        => name is null || name.Identifiers.Count == 0
            ? []
            : [new LockedTable(TableNames.Parts(name))];
}
