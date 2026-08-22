using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Rules.Rewrite;

/// <summary>
/// MSSQL-RW-010: DROP COLUMN is metadata-only, but the space is not reclaimed until
/// DBCC CLEANTABLE or an index rebuild runs. (Irreversibility of the drop is REV-001's job.)
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
                        fix: $"After the drop, run DBCC CLEANTABLE (0, '{table}'); or rebuild the " +
                             "clustered index to reclaim the space.");
                }
            }
        }
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
