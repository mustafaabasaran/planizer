using System.Globalization;
using Microsoft.Data.SqlClient;
using Planizer.MsSql;

namespace Planizer.CatalogVerification.Tests.Probes;

/// <summary>
/// Catalog row <c>add_check_or_fk</c> (any edition): <c>schm</c> / <c>full_scan</c> —
/// "WITH NOCHECK skips scan but constraint untrusted".
///
/// The declared expectation is <see cref="ProbeAspects.Blocking"/> only: a Sch-M table lock
/// must block both reads and writes while the constraint add is held open. The
/// <c>full_scan</c> movement class cannot be confirmed from log bytes by the evaluator, so the
/// scan-versus-skip claim and the trusted-flag claim are internal sanity checks instead: any
/// violation throws, which the runner degrades to Inconclusive with the message as evidence —
/// never a false Verified.
/// </summary>
public sealed class AddCheckOrFkProbe : CatalogProbeBase
{
    /// <summary>Very lenient floor of logical reads that still proves a validation scan ran.</summary>
    private const long ValidationScanMinimumReads = 100;

    public override string OperationKey => DdlOperationKeys.AddCheckOrFk;

    public override ProbeExpectation Expectation => new(ProbeAspects.Blocking);

    private string ConstraintName => $"CK_{TableName}";

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        var withCheckSql =
            $"ALTER TABLE {QualifiedTableName} WITH CHECK " +
            $"ADD CONSTRAINT [{ConstraintName}] CHECK (id > 0);";
        var withNoCheckSql =
            $"ALTER TABLE {QualifiedTableName} WITH NOCHECK " +
            $"ADD CONSTRAINT [{ConstraintName}] CHECK (id > 0);";
        var dropConstraintSql = $"ALTER TABLE {QualifiedTableName} DROP CONSTRAINT [{ConstraintName}];";

        await using var connection = await session.OpenConnectionAsync();

        // Internal check 1 — the validation scan: WITH CHECK must read the table, WITH NOCHECK
        // must skip it. Both adds are rolled back, leaving the table constraint-free.
        var withCheck = await Measurement.MeasureInRolledBackTransactionAsync(connection, withCheckSql);
        var withNoCheck = await Measurement.MeasureInRolledBackTransactionAsync(connection, withNoCheckSql);
        if (withCheck.SessionReadsDelta < ValidationScanMinimumReads
            || withNoCheck.SessionReadsDelta * 2 >= withCheck.SessionReadsDelta)
        {
            throw new InvalidOperationException(
                $"WITH CHECK should full-scan and WITH NOCHECK should skip the scan, but logical reads " +
                $"were {withCheck.SessionReadsDelta} (WITH CHECK) vs {withNoCheck.SessionReadsDelta} (WITH NOCHECK).");
        }

        // Internal check 2 — the trusted flag: a NOCHECK constraint must land untrusted, a
        // checked one trusted. Autocommitted adds, dropped again right away.
        var untrustedFlagSql =
            "SELECT CAST(is_not_trusted AS int) FROM sys.check_constraints " +
            $"WHERE name = '{ConstraintName}' AND parent_object_id = OBJECT_ID('{QualifiedTableName}');";
        await Measurement.ExecuteAsync(connection, withNoCheckSql, Measurement.LongCommandTimeoutSeconds);
        var afterNoCheck = await ObjectProbeSql.ScalarInt64Async(connection, untrustedFlagSql);
        await Measurement.ExecuteAsync(connection, dropConstraintSql);
        await Measurement.ExecuteAsync(connection, withCheckSql, Measurement.LongCommandTimeoutSeconds);
        var afterCheck = await ObjectProbeSql.ScalarInt64Async(connection, untrustedFlagSql);
        await Measurement.ExecuteAsync(connection, dropConstraintSql);
        if (afterNoCheck != 1 || afterCheck != 0)
        {
            throw new InvalidOperationException(
                $"Expected is_not_trusted=1 after WITH NOCHECK and 0 after WITH CHECK, " +
                $"but observed {afterNoCheck} and {afterCheck}.");
        }

        // The verdict-bearing measurement: the Sch-M of the constraint add, held open, must
        // block reads and writes alike.
        var profile = await Measurement.BlockingProfileAsync(
            session.OpenConnectionAsync,
            ddlSql: withCheckSql,
            readProbeSql: $"SELECT TOP (1) id FROM {QualifiedTableName};",
            // Self-assignment: side-effect free even though it autocommits when not blocked.
            writeProbeSql: $"UPDATE TOP (1) {QualifiedTableName} SET payload = payload;");
        return new ProbeObservation { Blocking = profile };
    }
}

/// <summary>
/// Catalog row <c>drop_table</c> (any edition): <c>schm</c> / <c>none</c>. While a session
/// holds an uncommitted DROP TABLE open, a second session can neither read nor write the
/// table — its statements queue behind the Sch-M lock. The <c>none</c> movement class cannot
/// be judged from log bytes, so only the blocking aspect is declared.
/// </summary>
public sealed class DropTableProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.DropTable;

    public override ProbeExpectation Expectation => new(ProbeAspects.Blocking);

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        var profile = await Measurement.BlockingProfileAsync(
            session.OpenConnectionAsync,
            ddlSql: $"DROP TABLE {QualifiedTableName};",
            readProbeSql: $"SELECT TOP (1) id FROM {QualifiedTableName};",
            // Self-assignment: side-effect free even though it autocommits when not blocked.
            writeProbeSql: $"UPDATE TOP (1) {QualifiedTableName} SET payload = payload;");
        return new ProbeObservation { Blocking = profile };
    }
}

/// <summary>
/// Catalog row <c>truncate_table</c> (any edition): <c>schm</c> / <c>deallocate</c> —
/// "rollback-able inside a transaction; fails with FK references".
///
/// Declared aspects: <see cref="ProbeAspects.Blocking"/> (Sch-M blocks reads and writes while
/// the TRUNCATE is held open) and <see cref="ProbeAspects.Error"/> (TRUNCATE against a table
/// referenced by a FOREIGN KEY must fail with error 4712, the number the plan fixes). The
/// <c>deallocate</c> movement class cannot be judged from log bytes, so the rollback-ability
/// claim is an internal sanity check: the row count must drop to zero inside the transaction
/// and be fully restored by ROLLBACK, otherwise the probe throws (→ Inconclusive).
/// </summary>
public sealed class TruncateTableProbe : CatalogProbeBase
{
    /// <summary>SQL Server error 4712: cannot truncate a table referenced by a FOREIGN KEY constraint.</summary>
    private const int TruncateBlockedByForeignKeyError = 4712;

    public override string OperationKey => DdlOperationKeys.TruncateTable;

    public override ProbeExpectation Expectation =>
        new(ProbeAspects.Blocking | ProbeAspects.Error, TruncateBlockedByForeignKeyError);

    private string ChildTableName => $"{TableName}_child";

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        var truncateSql = $"TRUNCATE TABLE {QualifiedTableName};";
        await using var connection = await session.OpenConnectionAsync();

        // Internal check — TRUNCATE is transactional: empty inside the transaction, fully
        // restored by ROLLBACK.
        long duringTran;
        await Measurement.ExecuteAsync(connection, "BEGIN TRAN;");
        try
        {
            await Measurement.ExecuteAsync(connection, truncateSql, Measurement.LongCommandTimeoutSeconds);
            duringTran = await ObjectProbeSql.CountRowsAsync(connection, QualifiedTableName);
        }
        finally
        {
            await Measurement.ExecuteAsync(connection, "IF @@TRANCOUNT > 0 ROLLBACK;");
        }

        var afterRollback = await ObjectProbeSql.CountRowsAsync(connection, QualifiedTableName);
        if (duringTran != 0 || afterRollback != RowCount)
        {
            throw new InvalidOperationException(
                $"TRUNCATE should be rollback-able: expected 0 rows inside the transaction and " +
                $"{RowCount} after ROLLBACK, but observed {duringTran} and {afterRollback}.");
        }

        // Error aspect — an (empty) referencing child table alone must make TRUNCATE fail.
        await Measurement.ExecuteAsync(connection, $"""
            CREATE TABLE dbo.[{ChildTableName}] (
                id int NOT NULL CONSTRAINT [PK_{ChildTableName}] PRIMARY KEY,
                parent_id int NOT NULL CONSTRAINT [FK_{ChildTableName}_parent]
                    REFERENCES {QualifiedTableName} (id));
            """);
        int? fkErrorNumber;
        try
        {
            fkErrorNumber = await Measurement.ErrorNumberOfAsync(connection, truncateSql);
        }
        finally
        {
            await Measurement.ExecuteAsync(connection, ProbeSql.DropTable(ChildTableName));
        }

        // Blocking aspect — the held-open TRUNCATE's Sch-M must block reads and writes. Runs
        // after the child is dropped again, so the TRUNCATE itself succeeds.
        var profile = await Measurement.BlockingProfileAsync(
            session.OpenConnectionAsync,
            ddlSql: truncateSql,
            readProbeSql: $"SELECT TOP (1) id FROM {QualifiedTableName};",
            // Self-assignment: side-effect free even though it autocommits when not blocked.
            writeProbeSql: $"UPDATE TOP (1) {QualifiedTableName} SET payload = payload;");

        return new ProbeObservation { Blocking = profile, Error = new ErrorObservation(fkErrorNumber) };
    }

    public override async Task CleanupAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        // The child must go first; its FOREIGN KEY blocks dropping the parent.
        await Measurement.ExecuteAsync(connection, ProbeSql.DropTable(ChildTableName));
        await Measurement.ExecuteAsync(connection, ProbeSql.DropTable(TableName));
    }
}

/// <summary>
/// Catalog row <c>sp_rename</c> (any edition): <c>schm_brief</c> / <c>metadata_only</c> —
/// "dependencies not updated (deferred name resolution)".
///
/// The declared expectation is <see cref="ProbeAspects.Movement"/>: renaming the table must
/// write metadata-level log only. The dependency claim is an internal sanity check: a
/// procedure selecting from the table must work before the rename and fail with some engine
/// error afterwards. The plan fixes no error number for that break (deferred name resolution
/// makes 208 the likely one), so any error counts as "broken" and only the absence of an
/// error throws (→ Inconclusive) — no guessed number can produce a false verdict.
/// </summary>
public sealed class SpRenameProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.SpRename;

    public override ProbeExpectation Expectation => new(ProbeAspects.Movement);

    private string RenamedTableName => $"{TableName}_renamed";

    private string ProcedureName => $"{TableName}_reader";

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        // sp_rename wants the current name possibly qualified and the new name bare.
        var renameSql = $"EXEC sp_rename 'dbo.{TableName}', '{RenamedTableName}';";
        var renameBackSql = $"EXEC sp_rename 'dbo.{RenamedTableName}', '{TableName}';";
        var execProcedureSql = $"EXEC dbo.[{ProcedureName}];";

        await using var connection = await session.OpenConnectionAsync();

        // The verdict-bearing measurement: the rename inside a rolled-back transaction must be
        // metadata-only. The rollback restores the original name.
        var measurement = await Measurement.MeasureInRolledBackTransactionAsync(connection, renameSql);

        // Internal check — the rename breaks dependents without touching them. Runs linearly;
        // CleanupAsync normalizes both possible table names if anything in between throws.
        await Measurement.ExecuteAsync(
            connection,
            $"CREATE PROCEDURE dbo.[{ProcedureName}] AS SELECT TOP (1) id FROM {QualifiedTableName};");
        await Measurement.ExecuteAsync(connection, execProcedureSql);
        await Measurement.ExecuteAsync(connection, renameSql);
        var brokenErrorNumber = await Measurement.ErrorNumberOfAsync(connection, execProcedureSql);
        await Measurement.ExecuteAsync(connection, renameBackSql);
        await Measurement.ExecuteAsync(connection, $"DROP PROCEDURE dbo.[{ProcedureName}];");
        if (brokenErrorNumber is null)
        {
            throw new InvalidOperationException(
                "The dependent procedure still executed after sp_rename; deferred name resolution " +
                "should have left it broken.");
        }

        return ProbeObservation.FromMeasurement(measurement, RowCount);
    }

    public override async Task CleanupAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        await Measurement.ExecuteAsync(connection, $"DROP PROCEDURE IF EXISTS dbo.[{ProcedureName}];");
        // A crash between rename and rename-back leaves the renamed table behind; drop both names.
        await Measurement.ExecuteAsync(connection, ProbeSql.DropTable(RenamedTableName));
        await Measurement.ExecuteAsync(connection, ProbeSql.DropTable(TableName));
    }
}

/// <summary>
/// Catalog row <c>alter_table_switch</c> (any edition): <c>schm_brief</c> / <c>metadata_only</c>.
/// Switching a non-partitioned table into an empty structural twin (the whole table is its
/// single partition) must write metadata-level log only, on every edition. The rollback of the
/// measuring transaction must put every row back into the source table (internal check).
/// </summary>
public sealed class AlterTableSwitchProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.AlterTableSwitch;

    public override ProbeExpectation Expectation => new(ProbeAspects.Movement);

    private string TargetTableName => $"{TableName}_target";

    public override async Task ArrangeAsync(ProbeSession session)
    {
        await base.ArrangeAsync(session);
        await using var connection = await session.OpenConnectionAsync();
        await Measurement.ExecuteAsync(connection, ProbeSql.DropTable(TargetTableName));
        // SWITCH needs an identical structure on the same filegroup, empty: this mirrors the
        // shape of ProbeSql.CreateFilledTable without the fill.
        await Measurement.ExecuteAsync(connection, $"""
            CREATE TABLE dbo.[{TargetTableName}] (
                id int NOT NULL CONSTRAINT [PK_{TargetTableName}] PRIMARY KEY,
                payload varchar(100) NOT NULL);
            """);
    }

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        var measurement = await Measurement.MeasureInRolledBackTransactionAsync(
            connection, $"ALTER TABLE {QualifiedTableName} SWITCH TO dbo.[{TargetTableName}];");

        var restored = await ObjectProbeSql.CountRowsAsync(connection, QualifiedTableName);
        if (restored != RowCount)
        {
            throw new InvalidOperationException(
                $"Rolling back the SWITCH should restore all {RowCount} source rows, " +
                $"but {restored} remained.");
        }

        return ProbeObservation.FromMeasurement(measurement, RowCount);
    }

    public override async Task CleanupAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        await Measurement.ExecuteAsync(connection, ProbeSql.DropTable(TargetTableName));
        await Measurement.ExecuteAsync(connection, ProbeSql.DropTable(TableName));
    }
}

/// <summary>
/// Catalog row <c>enable_disable_trigger</c> (any edition): <c>schm_brief</c> /
/// <c>metadata_only</c> — "flips the trigger's is_disabled flag; no data touched". Disabling
/// the trigger must write metadata-level log only; that the statement really just flips
/// <c>sys.triggers.is_disabled</c> in both directions is an internal sanity check.
/// </summary>
public sealed class EnableDisableTriggerProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.EnableDisableTrigger;

    public override ProbeExpectation Expectation => new(ProbeAspects.Movement);

    private string TriggerName => $"{TableName}_trg";

    public override async Task ArrangeAsync(ProbeSession session)
    {
        await base.ArrangeAsync(session);
        await using var connection = await session.OpenConnectionAsync();
        await Measurement.ExecuteAsync(
            connection,
            $"CREATE TRIGGER [{TriggerName}] ON {QualifiedTableName} AFTER UPDATE AS " +
            "BEGIN SET NOCOUNT ON; END;");
    }

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        var disableSql = $"DISABLE TRIGGER [{TriggerName}] ON {QualifiedTableName};";
        var enableSql = $"ENABLE TRIGGER [{TriggerName}] ON {QualifiedTableName};";
        var flagSql = $"SELECT CAST(is_disabled AS int) FROM sys.triggers WHERE name = '{TriggerName}';";

        await using var connection = await session.OpenConnectionAsync();

        // The verdict-bearing measurement: DISABLE TRIGGER inside a rolled-back transaction
        // must be metadata-only. The rollback leaves the trigger enabled.
        var measurement = await Measurement.MeasureInRolledBackTransactionAsync(connection, disableSql);

        // Internal check — the statements flip the flag in both directions (autocommitted).
        await Measurement.ExecuteAsync(connection, disableSql);
        var afterDisable = await ObjectProbeSql.ScalarInt64Async(connection, flagSql);
        await Measurement.ExecuteAsync(connection, enableSql);
        var afterEnable = await ObjectProbeSql.ScalarInt64Async(connection, flagSql);
        if (afterDisable != 1 || afterEnable != 0)
        {
            throw new InvalidOperationException(
                $"Expected is_disabled=1 after DISABLE and 0 after ENABLE, " +
                $"but observed {afterDisable} and {afterEnable}.");
        }

        return ProbeObservation.FromMeasurement(measurement, RowCount);
    }

    // No cleanup override: dropping the probe table drops its trigger with it.
}

/// <summary>Scalar helpers shared by the object probes in this file.</summary>
internal static class ObjectProbeSql
{
    public static async Task<long> ScalarInt64Async(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public static Task<long> CountRowsAsync(SqlConnection connection, string qualifiedTableName) =>
        ScalarInt64Async(connection, $"SELECT COUNT_BIG(*) FROM {qualifiedTableName};");
}
