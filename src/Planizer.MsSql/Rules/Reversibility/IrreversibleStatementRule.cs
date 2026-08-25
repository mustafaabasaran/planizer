using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

// The enum Planizer.MsSql.Reversibility is shadowed by this namespace's last segment.
using ReversibilityKind = Planizer.MsSql.Reversibility;

namespace Planizer.MsSql.Rules.Reversibility;

/// <summary>
/// MSSQL-REV-001: statements that destroy data no rollback script can bring back. DDL cases
/// (DROP TABLE, DROP COLUMN, TRUNCATE) are resolved through the behavior catalog
/// (<c>reversible=no</c>); a DELETE that nothing bounds is flagged directly — it is DML and
/// has no catalog row. "Nothing bounds it" includes a FROM clause whose join cannot drop target
/// rows (see <see cref="DmlTargets.ClassifyPersistentWrite"/>); where the join's effect depends on
/// the data the rule stays silent, since Critical on a guess would be wrong.
/// ALTER COLUMN narrowing is also irreversible but cannot be proven from the
/// script alone (the current type is unknown offline); the RW family carries that risk instead.
/// The fix follows the expand/contract pattern: rename first, drop a release later.
/// </summary>
public sealed class IrreversibleStatementRule : MsSqlRuleBase
{
    public const string RuleId = "MSSQL-REV-001";

    public override string Id => RuleId;
    public override string Title => "Statement is irreversible; data cannot be restored";
    public override Severity DefaultSeverity => Severity.Critical;

    protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
    {
        foreach (var statement in context.Statements)
        {
            if (DmlTargets.TargetsTransientObject(statement.Ast))
            {
                continue; // temp tables / table variables hold no persistent data
            }

            if (statement.Ast is DeleteStatement delete)
            {
                var bounds = DmlTargets.ClassifyPersistentWrite(delete.DeleteSpecification);
                if (bounds.Boundedness == JoinBoundedness.Unbounded)
                {
                    var table = Render(TargetTable(delete));

                    // A join that cannot drop target rows — an outer join with the target on its
                    // preserved side, a cross join, OUTER APPLY — reads as a filter but is none.
                    // Name it instead of claiming the DELETE has no WHERE clause.
                    yield return CreateFinding(statement, Severity.Critical,
                        bounds.Join is null
                            ? $"DELETE without a WHERE clause removes every row of {table} and cannot " +
                              "be undone; keep a copy first and delete in batches."
                            : $"The {bounds.Join} does not restrict {table}; every row is deleted and " +
                              "cannot be restored — keep a copy first and delete in batches.",
                        fix: $"SELECT * INTO {table}_backup FROM {table}; -- verify, then delete in batches with a WHERE clause");
                }

                continue;
            }

            if (statement.Kind != StatementKind.Ddl || !IsIrreversibleDdl(statement, context))
            {
                continue;
            }

            yield return statement.Ast switch
            {
                DropTableStatement drop => CreateFinding(statement, Severity.Critical,
                    $"DROP TABLE cannot be undone; rename {Render(FirstTable(drop))} to " +
                    $"'{BaseName(FirstTable(drop))}_deprecated' and drop it in a later release.",
                    fix: RenameTableFix(drop)),

                AlterTableDropTableElementStatement dropColumn => CreateFinding(statement, Severity.Critical,
                    "DROP COLUMN cannot be undone; rename the column(s) to '<name>_deprecated' " +
                    "and drop them in a later release.",
                    fix: RenameColumnsFix(dropColumn)),

                _ => CreateFinding(statement, Severity.Critical, // TRUNCATE TABLE
                    $"TRUNCATE TABLE removes every row of {TruncateTarget(statement)} and the data " +
                    "cannot be restored after commit; keep a copy first.",
                    fix: $"SELECT * INTO {TruncateTarget(statement)}_backup FROM {TruncateTarget(statement)}; -- verify, then truncate"),
            };
        }
    }

    /// <summary>Catalog verdict for the DDL statement: only an explicit <c>reversible=no</c> row counts.</summary>
    private static bool IsIrreversibleDdl(SqlStatementInfo statement, MsSqlAnalysisContext context)
        => DdlOperationClassifier.GetBehavior(statement, context.Catalog, context.Config) is
            { Reversible: ReversibilityKind.No };

    private static SchemaObjectName? TargetTable(DeleteStatement delete)
        => DmlTargets.ResolveTargetTable(delete.DeleteSpecification);

    private static SchemaObjectName? FirstTable(DropTableStatement drop)
        => drop.Objects.FirstOrDefault();

    private static string TruncateTarget(SqlStatementInfo statement)
        => Render((statement.Ast as TruncateTableStatement)?.TableName);

    private static string RenameTableFix(DropTableStatement drop)
        => string.Join('\n', drop.Objects.Select(o =>
            $"EXEC sp_rename '{Render(o)}', '{BaseName(o)}_deprecated';"));

    private static string? RenameColumnsFix(AlterTableDropTableElementStatement dropColumn)
    {
        var table = Render(dropColumn.SchemaObjectName);
        var renames = dropColumn.AlterTableDropTableElements
            .Where(e => e.Name?.Value is not null)
            .Select(e => $"EXEC sp_rename '{table}.{e.Name.Value}', '{e.Name.Value}_deprecated', 'COLUMN';")
            .ToList();
        return renames.Count == 0 ? null : string.Join('\n', renames);
    }

    private static string Render(SchemaObjectName? name)
        => name is null || name.Identifiers.Count == 0
            ? "the table"
            : string.Join(".", name.Identifiers.Select(i => i.Value));

    private static string BaseName(SchemaObjectName? name)
        => name?.BaseIdentifier?.Value ?? "table";
}
