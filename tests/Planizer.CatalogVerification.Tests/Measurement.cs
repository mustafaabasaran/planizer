using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Planizer.CatalogVerification.Tests;

/// <summary>Log and read deltas of one act statement executed inside a rolled-back transaction.</summary>
public readonly record struct ActMeasurement(long LogBytesDelta, long SessionReadsDelta);

/// <summary>
/// Which of a second session's probes timed out while the first session held the DDL's
/// transaction open. A schema-modification (Sch-M) table lock shows as <c>(true, true)</c>,
/// a shared (S) table lock as <c>(false, true)</c> — reads allowed, writes blocked, exactly the
/// semantics documented in <c>docs/rules/MSSQL-LOCK-002.md</c> — and no table lock of note as
/// <c>(false, false)</c>.
/// </summary>
public readonly record struct BlockingProfile(bool ReadsBlocked, bool WritesBlocked);

/// <summary>Movement classification of a measured transaction-log delta.</summary>
public enum ObservedMovement
{
    MetadataOnly,
    Rewrite,
    Inconclusive,
}

/// <summary>
/// Measurement primitives for catalog probes. The classification helpers are pure and unit
/// tested without a server; the async primitives require a live connection and therefore only
/// ever run behind <see cref="VerificationGate"/> in CI.
/// </summary>
public static class Measurement
{
    /// <summary>Standard probe table size; the classification thresholds assume it.</summary>
    public const int DefaultProbeRowCount = 100_000;

    /// <summary>Below this log delta the engine wrote metadata only (64 KB).</summary>
    public const long MetadataOnlyLogCeilingBytes = 64 * 1024;

    /// <summary>Lock timeout used by the blocked-or-not probes of <see cref="BlockingProfileAsync"/>.</summary>
    public const int DefaultLockTimeoutMs = 800;

    /// <summary>Command timeout for arrange scripts and potentially size-of-data act statements.</summary>
    public const int LongCommandTimeoutSeconds = 300;

    /// <summary>SQL Server error 1222: lock request time out period exceeded.</summary>
    public const int LockRequestTimeoutErrorNumber = 1222;

    private const string TranLogBytesQuery = """
        SELECT COALESCE(SUM(database_transaction_log_bytes_used), CAST(0 AS bigint))
        FROM sys.dm_tran_database_transactions
        WHERE transaction_id = CURRENT_TRANSACTION_ID() AND database_id = DB_ID();
        """;

    private const string SessionReadsQuery =
        "SELECT logical_reads FROM sys.dm_exec_sessions WHERE session_id = @@SPID;";

    /// <summary>Above 8 bytes per row of log the whole table was rewritten.</summary>
    public static long RewriteLogFloorBytes(int rowCount) => rowCount * 8L;

    /// <summary>
    /// Classifies the transaction-log delta of one DDL statement against a probe table of
    /// <paramref name="rowCount"/> rows: below 64 KB is metadata-only, above 8 bytes per row is
    /// a rewrite, anything between is inconclusive. The bands assume the standard 100k-row probe
    /// table (floor 800,000 B), where they cannot overlap.
    /// </summary>
    public static ObservedMovement ClassifyLogBytes(long logBytesDelta, int rowCount) =>
        logBytesDelta < MetadataOnlyLogCeilingBytes ? ObservedMovement.MetadataOnly
        : logBytesDelta > RewriteLogFloorBytes(rowCount) ? ObservedMovement.Rewrite
        : ObservedMovement.Inconclusive;

    /// <summary>Executes a batch on the given open connection.</summary>
    public static async Task ExecuteAsync(SqlConnection connection, string sql, int timeoutSeconds = 30)
    {
        using var command = new SqlCommand(sql, connection) { CommandTimeout = timeoutSeconds };
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Runs the act statement inside <c>BEGIN TRAN … ROLLBACK</c> on one connection and returns
    /// the deltas of <c>database_transaction_log_bytes_used</c> (data movement evidence) and the
    /// session's <c>logical_reads</c> (validation-scan evidence). The rollback leaves the probe
    /// table exactly as arranged.
    /// </summary>
    public static async Task<ActMeasurement> MeasureInRolledBackTransactionAsync(
        SqlConnection connection, string actSql, int timeoutSeconds = LongCommandTimeoutSeconds)
    {
        await ExecuteAsync(connection, "BEGIN TRAN;");
        try
        {
            var logBefore = await ScalarInt64Async(connection, TranLogBytesQuery);
            var readsBefore = await ScalarInt64Async(connection, SessionReadsQuery);
            await ExecuteAsync(connection, actSql, timeoutSeconds);
            var logAfter = await ScalarInt64Async(connection, TranLogBytesQuery);
            var readsAfter = await ScalarInt64Async(connection, SessionReadsQuery);
            return new ActMeasurement(logAfter - logBefore, readsAfter - readsBefore);
        }
        finally
        {
            await ExecuteAsync(connection, "IF @@TRANCOUNT > 0 ROLLBACK;");
        }
    }

    /// <summary>Transaction-log bytes written by the act statement (rolled back afterwards).</summary>
    public static async Task<long> LogBytesOfAsync(SqlConnection connection, string actSql) =>
        (await MeasureInRolledBackTransactionAsync(connection, actSql)).LogBytesDelta;

    /// <summary>Logical reads performed by the act statement on the same session (rolled back afterwards).</summary>
    public static async Task<long> SessionReadsOfAsync(SqlConnection connection, string actSql) =>
        (await MeasureInRolledBackTransactionAsync(connection, actSql)).SessionReadsDelta;

    /// <summary>
    /// Measures the lock profile of a DDL statement: connection A runs it inside an open
    /// transaction (so the locks it acquired stay held), then connection B — under
    /// <c>SET LOCK_TIMEOUT</c> — first tries the read probe, then the write probe. A probe that
    /// fails with error 1222 counts as blocked. A then rolls back, undoing the DDL.
    /// Because the transaction is held open, "brief" locks are retained too: the profile
    /// verifies the lock <em>category</em> (Sch-M vs S vs none), not its duration.
    /// The write probe must be side-effect free (e.g. a self-assigning <c>UPDATE</c>), because
    /// it autocommits when it is not blocked.
    /// </summary>
    public static async Task<BlockingProfile> BlockingProfileAsync(
        Func<Task<SqlConnection>> openConnection,
        string ddlSql,
        string readProbeSql,
        string writeProbeSql,
        int lockTimeoutMs = DefaultLockTimeoutMs)
    {
        await using var holder = await openConnection();
        await ExecuteAsync(holder, "BEGIN TRAN;");
        try
        {
            await ExecuteAsync(holder, ddlSql, LongCommandTimeoutSeconds);

            await using var prober = await openConnection();
            await ExecuteAsync(prober, $"SET LOCK_TIMEOUT {lockTimeoutMs};");
            var readsBlocked = await TimesOutAsync(prober, readProbeSql);
            var writesBlocked = await TimesOutAsync(prober, writeProbeSql);
            return new BlockingProfile(readsBlocked, writesBlocked);
        }
        finally
        {
            await ExecuteAsync(holder, "IF @@TRANCOUNT > 0 ROLLBACK;");
        }
    }

    /// <summary>
    /// Runs the act statement (inside a rolled-back transaction) and reports the
    /// <see cref="SqlException.Number"/> it raised, or <c>null</c> when it succeeded.
    /// </summary>
    public static async Task<int?> ErrorNumberOfAsync(
        SqlConnection connection, string actSql, int timeoutSeconds = LongCommandTimeoutSeconds)
    {
        await ExecuteAsync(connection, "BEGIN TRAN;");
        try
        {
            await ExecuteAsync(connection, actSql, timeoutSeconds);
            return null;
        }
        catch (SqlException exception)
        {
            return exception.Number;
        }
        finally
        {
            await ExecuteAsync(connection, "IF @@TRANCOUNT > 0 ROLLBACK;");
        }
    }

    /// <summary>Whether the act statement fails with exactly the expected error number.</summary>
    public static async Task<(bool Matched, int? ActualErrorNumber)> ExpectErrorAsync(
        SqlConnection connection, string actSql, int expectedErrorNumber)
    {
        var actual = await ErrorNumberOfAsync(connection, actSql);
        return (actual == expectedErrorNumber, actual);
    }

    private static async Task<bool> TimesOutAsync(SqlConnection connection, string sql)
    {
        try
        {
            await ExecuteAsync(connection, sql);
            return false;
        }
        catch (SqlException exception) when (exception.Number == LockRequestTimeoutErrorNumber)
        {
            return true;
        }
    }

    private static async Task<long> ScalarInt64Async(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// SQL builders for probe tables. Every probe gets its own permanent (non-temp) table named
/// <c>probe_&lt;operation_key&gt;</c>, filled with the standard row count in a single
/// <c>INSERT … SELECT</c> over <c>GENERATE_SERIES</c> (SQL Server 2022+), and drops it afterwards.
/// </summary>
public static class ProbeSql
{
    public static string TableNameFor(string operationKey) => $"probe_{operationKey}";

    public static string CreateFilledTable(string tableName, int rowCount) => $"""
        CREATE TABLE dbo.[{tableName}] (
            id int NOT NULL CONSTRAINT [PK_{tableName}] PRIMARY KEY,
            payload varchar(100) NOT NULL);
        INSERT INTO dbo.[{tableName}] (id, payload)
        SELECT value, CONCAT('row-', value)
        FROM GENERATE_SERIES(1, {rowCount});
        """;

    public static string DropTable(string tableName) => $"DROP TABLE IF EXISTS dbo.[{tableName}];";
}
