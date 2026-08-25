using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-010: DROP COLUMN is metadata-only, but the space is not reclaimed until
/// DBCC CLEANTABLE or an index rebuild runs. (Irreversibility of the drop is REV-001's job.)
/// Which of the two works is not interchangeable: DBCC CLEANTABLE "doesn't reclaim space after a
/// fixed-length column is dropped", so an int/datetime/char column needs the rebuild and
/// CLEANTABLE would be a fully logged no-op. The dropped column's type is unknown at this
/// text-only layer, so the fix states both branches; CLEANTABLE is also unsupported on temporary
/// tables and is left out entirely for a <c>#</c>/<c>##</c> target.
/// </summary>
public sealed class DropColumnRule : MsSqlRuleBase
{
    public override string Id => "MSSQL-RW-010";
    public override string Title => "Dropping a column does not reclaim its space";
    public override Severity DefaultSeverity => Severity.Warning;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (statement.Ast is not AlterTableDropTableElementStatement drop)
            {
                continue;
            }

            var table = SqlNames.Table(drop.SchemaObjectName);
            foreach (var column in DroppedColumns(drop))
            {
                var behavior = context.Catalog.Lookup(
                    DdlOperationKeys.DropColumn, context.Config.TargetVersion, context.Config.Edition);

                if (behavior is null)
                {
                    yield return CreateFinding(statement, Severity.Warning,
                        $"No behavior data for dropping column {column} from {table} " +
                        "under the configured target — review manually.",
                        inconclusive: true);
                }
                else
                {
                    yield return CreateFinding(statement, Severity.Warning,
                        $"Dropping column {column} from {table} is metadata-only, but its space " +
                        "is not reclaimed until DBCC CLEANTABLE or an index rebuild runs.",
                        fix: ReclaimFix(table, DmlTargets.IsTransient(drop.SchemaObjectName)));
                }
            }
        }
    }

    /// <summary>
    /// How to reclaim the space, split by what actually works. DBCC CLEANTABLE only releases the
    /// bytes of dropped variable-length and LOB columns; after a fixed-length column it reclaims
    /// nothing, and it is not supported on temporary tables at all.
    /// </summary>
    private static string ReclaimFix(string table, bool isTempTable)
    {
        var rebuild = $"ALTER INDEX ALL ON {table} REBUILD; (locking: MSSQL-LOCK-006)";

        return isTempTable
            ? "After the drop, rebuild to reclaim the space — DBCC CLEANTABLE is not supported on "
              + $"temporary tables:\n{rebuild}"
            : "After the drop, reclaim the space with the operation that matches the dropped "
              + "column's type:\n"
              + "Variable-length or LOB (varchar, nvarchar, varbinary, text, ntext, image, "
              + $"sql_variant, xml and their max variants): DBCC CLEANTABLE (0, '{table}');\n"
              + "Fixed-length (int, bigint, datetime, char, decimal, uniqueidentifier and the like): "
              + "CLEANTABLE reclaims nothing there — it is a fully logged no-op — so rebuild instead: "
              + rebuild;
    }

    /// <summary>
    /// Names of the dropped columns. In "DROP COLUMN A, B" only the first element carries the
    /// COLUMN keyword; the rest are NotSpecified and inherit the preceding element's kind.
    /// </summary>
    private static IEnumerable<string> DroppedColumns(AlterTableDropTableElementStatement drop)
    {
        var inherited = TableElementType.NotSpecified;
        foreach (var element in drop.AlterTableDropTableElements)
        {
            var type = element.TableElementType == TableElementType.NotSpecified
                ? inherited
                : element.TableElementType;
            inherited = type;

            if (type == TableElementType.Column)
            {
                yield return element.Name?.Value ?? "the column";
            }
        }
    }
}
