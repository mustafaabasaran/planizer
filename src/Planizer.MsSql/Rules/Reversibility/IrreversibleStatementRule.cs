using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql.Parsing;

// The enum Planizer.MsSql.Reversibility is shadowed by this namespace's last segment.
using ReversibilityKind = Planizer.MsSql.Reversibility;

namespace Planizer.MsSql.Rules.Reversibility;

/// <summary>
/// MSSQL-REV-001: statements that destroy data no rollback script can bring back. DDL cases
/// (DROP TABLE, DROP COLUMN, TRUNCATE) are resolved through the behavior catalog
/// (<c>reversible=no</c>); a DELETE without a WHERE clause is flagged directly — it is DML and
/// has no catalog row. ALTER COLUMN narrowing is also irreversible but cannot be proven from the
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
                if (DmlTargets.IsUnboundedPersistentWrite(delete.DeleteSpecification))
                {
                    var table = Render(TargetTable(delete));
                    yield return CreateFinding(statement, Severity.Critical,
                        $"DELETE without a WHERE clause removes every row of {table} and cannot be undone; " +
                        "keep a copy first and delete in batches.",
                        fix: BackupCopyFix(table, "then delete in batches with a WHERE clause"));
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
                    fix: BackupCopyFix(TruncateTarget(statement), "then truncate")),
            };
        }
    }

    /// <summary>
    /// Keep-a-copy-first suggestion for the two wipe cases (unbounded DELETE, TRUNCATE). The target
    /// name is date-stamped because a fixed <c>_backup</c> suffix already exists the second time
    /// the migration runs and <c>SELECT … INTO</c> then fails with error 2714 — a poor look in a
    /// tool that ships three idempotency rules. The copy is deliberately labelled data-only:
    /// "Indexes, constraints, and triggers defined in the source table aren't transferred to the
    /// new table", which is exactly what a restore path wants — the originals still live on the
    /// source table.
    /// </summary>
    private static string BackupCopyFix(string table, string thenWhat)
        => "-- Data-only copy: SELECT … INTO transfers no indexes, constraints or triggers, which is what a restore path needs.\n"
           + "-- Date-stamp the target; a fixed _backup name already exists on a re-run and fails with error 2714.\n"
           + $"SELECT * INTO {table}_backup_<yyyymmdd> FROM {table};\n"
           + $"-- Verify the copy, {thenWhat}.";

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
