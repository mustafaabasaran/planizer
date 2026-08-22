using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql;

/// <summary>
/// Recognises the "check the catalog first" pattern that makes a DDL statement safe to re-run:
/// <c>IF NOT EXISTS (SELECT … FROM sys.columns …)</c>, <c>IF OBJECT_ID(…) IS NULL</c>,
/// <c>IF COL_LENGTH(…) IS NULL</c> and friends. The heuristic is deliberately generous — any
/// <c>EXISTS</c>, any catalog function, any <c>sys.*</c> / <c>INFORMATION_SCHEMA.*</c> /
/// <c>sys*</c> compatibility view in the predicate counts — so the idempotency rules err on the
/// side of silence. Two shapes are recognised: the <b>enclosing</b> IF (<c>IF … BEGIN CREATE …
/// END</c>) and the <b>exit guard</b> that precedes the statement in the same batch
/// (<c>IF OBJECT_ID('dbo.T') IS NOT NULL RETURN;</c> followed by a bare <c>CREATE TABLE</c>).
/// </summary>
public static class IdempotencyGuard
{
    private static readonly HashSet<string> CatalogFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "OBJECT_ID", "OBJECT_NAME", "OBJECTPROPERTY", "OBJECTPROPERTYEX",
        "COL_LENGTH", "COL_NAME", "COLUMNPROPERTY",
        "INDEXPROPERTY", "INDEX_COL", "INDEXKEY_PROPERTY",
        "TYPE_ID", "TYPE_NAME", "TYPEPROPERTY",
        "SCHEMA_ID", "SCHEMA_NAME",
        "DB_ID", "DB_NAME", "DATABASEPROPERTYEX",
        "FILE_ID", "FILE_NAME", "FILEGROUP_ID", "FILEGROUP_NAME",
        "SUSER_ID", "SUSER_SID", "USER_ID", "DATABASE_PRINCIPAL_ID",
        "SERVERPROPERTY", "FULLTEXTCATALOGPROPERTY", "FULLTEXTSERVICEPROPERTY",
    };

    /// <summary>
    /// Whether the statement sits (at any depth) inside an <c>IF</c> whose predicate queries the
    /// catalog. Both branches count: <c>IF EXISTS … ELSE CREATE …</c> guards the ELSE. Every
    /// enclosing IF is inspected, so an unrelated inner <c>IF @flag = 1</c> does not hide an outer
    /// <c>IF OBJECT_ID(…) IS NULL</c>.
    /// </summary>
    public static bool IsGuarded(SqlStatementInfo statement)
    {
        for (var ancestor = statement.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.Ast is IfStatement { Predicate: { } predicate } && QueriesCatalog(predicate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <see cref="IsGuarded(SqlStatementInfo)"/> plus the early-exit idiom: an earlier <c>IF</c> in
    /// the <b>same batch</b> whose predicate queries the catalog and whose THEN branch leaves the
    /// batch — <c>RETURN</c>, <c>THROW</c> or <c>GOTO</c>, looking through <c>BEGIN…END</c> —
    /// guards everything that follows it in that batch (<c>RETURN</c> ends the batch, so the
    /// statements after it never run when the object exists). The exit guard has to be a sibling
    /// of the statement or of one of its enclosing wrappers: one inside another IF's branch
    /// guards only that branch. <c>RAISERROR</c> does not count — even at severity 16 the next
    /// statement still runs.
    /// </summary>
    public static bool IsGuarded(SqlStatementInfo statement, MsSqlAnalysisContext context)
        => IsGuarded(statement) || HasExitGuardBefore(statement, context);

    /// <summary>
    /// Whether an earlier statement in the same file drops <paramref name="target"/> safely —
    /// <c>DROP … IF EXISTS</c>, or a plain <c>DROP</c> that is itself <see cref="IsGuarded"/>.
    /// A <c>DROP INDEX</c> matches by index name. Object names compare case-insensitively; when
    /// either side omits the schema only the base name is compared.
    /// </summary>
    public static bool IsDroppedEarlierInFile(
        SqlStatementInfo statement,
        MsSqlAnalysisContext context,
        SchemaObjectName target)
    {
        foreach (var earlier in context.StatementsInFile(statement.Location.File))
        {
            if (earlier.Index >= statement.Index)
            {
                break;
            }

            var dropsTarget = earlier.Ast switch
            {
                DropObjectsStatement drop => drop.Objects.Any(o => SameObject(o, target))
                    && (drop.IsIfExists || IsGuarded(earlier, context)),
                DropIndexStatement dropIndex => dropIndex.DropIndexClauses
                        .OfType<DropIndexClause>()
                        .Any(c => SameIdentifier(c.Index, target.BaseIdentifier))
                    && (dropIndex.IsIfExists || IsGuarded(earlier, context)),
                _ => false,
            };

            if (dropsTarget)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a boolean expression touches the catalog (see class remarks for the heuristic).</summary>
    public static bool QueriesCatalog(BooleanExpression predicate)
    {
        var visitor = new CatalogReferenceVisitor();
        predicate.Accept(visitor);
        return visitor.Found;
    }

    private static bool HasExitGuardBefore(SqlStatementInfo statement, MsSqlAnalysisContext context)
    {
        var earlierInBatch = context.StatementsInBatch(statement.BatchIndex)
            .TakeWhile(s => s.Index < statement.Index)
            .ToList();

        if (earlierInBatch.Count == 0)
        {
            return false;
        }

        var children = earlierInBatch
            .Where(s => s.Parent is not null)
            .ToLookup(s => s.Parent!);

        // The guard must precede the statement — or one of its enclosing wrappers — in the
        // same statement list, i.e. share its Parent.
        for (var scope = statement; scope is not null; scope = scope.Parent)
        {
            foreach (var earlier in earlierInBatch)
            {
                if (earlier.Index >= scope.Index)
                {
                    break;
                }

                if (ReferenceEquals(earlier.Parent, scope.Parent) && IsExitGuard(earlier, children))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>An <c>IF</c> with a catalog predicate whose THEN branch leaves the batch.</summary>
    private static bool IsExitGuard(
        SqlStatementInfo candidate,
        ILookup<SqlStatementInfo, SqlStatementInfo> children)
        => candidate.Ast is IfStatement { Predicate: { } predicate }
           && QueriesCatalog(predicate)
           && LeavesBatch(children[candidate].Where(c => !c.InElseBranch), children);

    private static bool LeavesBatch(
        IEnumerable<SqlStatementInfo> branch,
        ILookup<SqlStatementInfo, SqlStatementInfo> children)
        => branch.Any(s => s.Ast is ReturnStatement or ThrowStatement or GoToStatement
            || (s.Ast is BeginEndBlockStatement && LeavesBatch(children[s], children)));

    private static bool SameObject(SchemaObjectName? a, SchemaObjectName? b)
    {
        if (a?.BaseIdentifier is null || b?.BaseIdentifier is null
            || !SameIdentifier(a.BaseIdentifier, b.BaseIdentifier))
        {
            return false;
        }

        return a.SchemaIdentifier is null
            || b.SchemaIdentifier is null
            || SameIdentifier(a.SchemaIdentifier, b.SchemaIdentifier);
    }

    private static bool SameIdentifier(Identifier? a, Identifier? b)
        => a?.Value is { } left && b?.Value is { } right
           && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private sealed class CatalogReferenceVisitor : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(ExistsPredicate node) => Found = true;

        public override void Visit(FunctionCall node)
        {
            if (node.FunctionName?.Value is { } name && CatalogFunctions.Contains(name))
            {
                Found = true;
            }
        }

        public override void Visit(NamedTableReference node)
        {
            var schema = node.SchemaObject?.SchemaIdentifier?.Value;
            var table = node.SchemaObject?.BaseIdentifier?.Value;

            if (string.Equals(schema, "sys", StringComparison.OrdinalIgnoreCase)
                || string.Equals(schema, "INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase)
                || (schema is null && table is not null
                    && table.StartsWith("sys", StringComparison.OrdinalIgnoreCase)))
            {
                Found = true; // sys.tables, INFORMATION_SCHEMA.COLUMNS, sysobjects, sysindexes, …
            }
        }
    }
}
