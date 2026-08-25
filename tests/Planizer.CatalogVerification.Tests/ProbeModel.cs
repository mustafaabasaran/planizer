using System.Globalization;
using Microsoft.Data.SqlClient;
using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.CatalogVerification.Tests;

/// <summary>
/// Outcome of comparing one probe's measurement against its catalog row. Inconclusive is a
/// first-class citizen: measurements written without a live server can fail in surprising ways
/// on the first CI run, and only <see cref="Contradicted"/> may fail the job.
/// </summary>
public enum VerdictKind
{
    Verified,
    Contradicted,
    Inconclusive,
}

/// <summary>A verdict plus the human-readable evidence that produced it.</summary>
public sealed record ProbeVerdict(VerdictKind Kind, string Evidence);

/// <summary>Aspects of a catalog row a probe can verify.</summary>
[Flags]
public enum ProbeAspects
{
    None = 0,

    /// <summary>The <c>data_movement</c> column, via transaction-log deltas.</summary>
    Movement = 1,

    /// <summary>The <c>lock</c> column, via a two-session blocking profile.</summary>
    Blocking = 2,

    /// <summary>An expected engine error number (e.g. <c>fails_if_rows</c> rows).</summary>
    Error = 4,
}

/// <summary>
/// What the probe claims to verify. <paramref name="ErrorNumber"/> is required when
/// <see cref="ProbeAspects.Error"/> is declared.
/// </summary>
public sealed record ProbeExpectation(ProbeAspects Aspects, int? ErrorNumber = null);

/// <summary>The error outcome of an error-expectation act: the raised number, or null on success.</summary>
public readonly record struct ErrorObservation(int? Number);

/// <summary>Everything a probe measured; unobserved aspects stay null.</summary>
public sealed record ProbeObservation
{
    public ObservedMovement? Movement { get; init; }

    public long? LogBytesDelta { get; init; }

    public long? SessionReadsDelta { get; init; }

    public BlockingProfile? Blocking { get; init; }

    public ErrorObservation? Error { get; init; }

    public static ProbeObservation FromMeasurement(ActMeasurement measurement, int rowCount) => new()
    {
        Movement = Measurement.ClassifyLogBytes(measurement.LogBytesDelta, rowCount),
        LogBytesDelta = measurement.LogBytesDelta,
        SessionReadsDelta = measurement.SessionReadsDelta,
    };
}

/// <summary>
/// One empirical check of one <c>mssql-ddl-behavior.csv</c> row. Probes are discovered by
/// reflection, so adding a probe never edits a shared file.
/// </summary>
public interface ICatalogProbe
{
    /// <summary>The <c>operation_key</c> of the catalog row this probe verifies.</summary>
    string OperationKey { get; }

    /// <summary>Which aspects of the row the measurement is able to confirm or contradict.</summary>
    ProbeExpectation Expectation { get; }

    /// <summary>Whether the probe can run at all on the given edition.</summary>
    bool AppliesTo(SqlEdition edition);

    /// <summary>Creates the probe's own objects (its <c>probe_&lt;key&gt;</c> table).</summary>
    Task ArrangeAsync(ProbeSession session);

    /// <summary>Performs the measurement and returns what was observed.</summary>
    Task<ProbeObservation> ActAsync(ProbeSession session);

    /// <summary>Drops the probe's objects; runs even when the act crashed.</summary>
    Task CleanupAsync(ProbeSession session);
}

/// <summary>The server a probe run talks to: connection factory plus the resolved catalog target.</summary>
public sealed class ProbeSession
{
    private readonly Func<Task<SqlConnection>> _openConnection;

    public ProbeSession(SqlEdition edition, SqlServerVersion version, Func<Task<SqlConnection>> openConnection)
    {
        Edition = edition;
        Version = version;
        _openConnection = openConnection;
    }

    /// <summary>Catalog edition of the server under test (Developer maps to Enterprise).</summary>
    public SqlEdition Edition { get; }

    /// <summary>Catalog version of the server under test.</summary>
    public SqlServerVersion Version { get; }

    public Task<SqlConnection> OpenConnectionAsync() => _openConnection();

    public static ProbeSession From(ServerFixture server) =>
        new(server.Edition, server.Version, server.OpenConnectionAsync);
}

/// <summary>One probe's reported row: verdict is null when the probe did not apply to the edition.</summary>
public sealed record ProbeOutcome(string OperationKey, string ProbeName, VerdictKind? Verdict, string Evidence);

/// <summary>
/// Base for probes that operate on a single standard probe table (100k rows, created in
/// Arrange, dropped in Cleanup).
/// </summary>
public abstract class CatalogProbeBase : ICatalogProbe
{
    public abstract string OperationKey { get; }

    public abstract ProbeExpectation Expectation { get; }

    protected string TableName => ProbeSql.TableNameFor(OperationKey);

    protected string QualifiedTableName => $"dbo.[{TableName}]";

    protected virtual int RowCount => Measurement.DefaultProbeRowCount;

    public virtual bool AppliesTo(SqlEdition edition) => true;

    public virtual async Task ArrangeAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        await Measurement.ExecuteAsync(connection, ProbeSql.DropTable(TableName));
        await Measurement.ExecuteAsync(
            connection, ProbeSql.CreateFilledTable(TableName, RowCount), Measurement.LongCommandTimeoutSeconds);
    }

    public abstract Task<ProbeObservation> ActAsync(ProbeSession session);

    public virtual async Task CleanupAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        await Measurement.ExecuteAsync(connection, ProbeSql.DropTable(TableName));
    }
}

/// <summary>
/// Compares a probe's observation against its catalog row, aspect by aspect. Any mismatch makes
/// the verdict Contradicted; otherwise any aspect that could not be judged makes it
/// Inconclusive; only fully confirmed observations are Verified.
/// </summary>
public static class VerdictEvaluator
{
    public static ProbeVerdict Evaluate(DdlBehavior catalogRow, ProbeExpectation expectation, ProbeObservation observation)
    {
        if (expectation.Aspects == ProbeAspects.None)
        {
            return new ProbeVerdict(VerdictKind.Inconclusive, "probe declares no verifiable aspect");
        }

        var matches = new List<string>();
        var mismatches = new List<string>();
        var unknowns = new List<string>();

        if (expectation.Aspects.HasFlag(ProbeAspects.Movement))
        {
            EvaluateMovement(catalogRow, observation, matches, mismatches, unknowns);
        }

        if (expectation.Aspects.HasFlag(ProbeAspects.Blocking))
        {
            EvaluateBlocking(catalogRow, observation, matches, mismatches, unknowns);
        }

        if (expectation.Aspects.HasFlag(ProbeAspects.Error))
        {
            EvaluateError(expectation, observation, matches, mismatches, unknowns);
        }

        var evidence = string.Join("; ", mismatches.Concat(unknowns).Concat(matches));
        var kind = mismatches.Count > 0 ? VerdictKind.Contradicted
            : unknowns.Count > 0 ? VerdictKind.Inconclusive
            : VerdictKind.Verified;
        return new ProbeVerdict(kind, evidence);
    }

    /// <summary>
    /// The blocking profile a catalog lock level implies while the DDL's transaction is held
    /// open. Consistent with the verified semantics of <c>docs/rules/MSSQL-LOCK-002.md</c> and
    /// <c>MSSQL-LOCK-004.md</c>: any Sch-M table lock blocks reads and writes; any S table lock
    /// — including the brief S of an online nonclustered build, retained by the open
    /// transaction — blocks writes but not reads. Held-open retention means the profile asserts
    /// the lock category, never its "brief vs duration" distinction.
    /// </summary>
    public static BlockingProfile ExpectedBlockingProfile(LockLevel lockLevel) => lockLevel switch
    {
        LockLevel.SchM or LockLevel.SchMBrief => new BlockingProfile(ReadsBlocked: true, WritesBlocked: true),
        LockLevel.STable or LockLevel.SBrief => new BlockingProfile(ReadsBlocked: false, WritesBlocked: true),
        LockLevel.None => new BlockingProfile(ReadsBlocked: false, WritesBlocked: false),
        _ => throw new ArgumentOutOfRangeException(nameof(lockLevel), lockLevel, "Unknown lock level."),
    };

    private static void EvaluateMovement(
        DdlBehavior row, ProbeObservation observation,
        List<string> matches, List<string> mismatches, List<string> unknowns)
    {
        if (observation.Movement is not { } observed)
        {
            unknowns.Add("movement: declared by the probe but not observed");
            return;
        }

        var logDetail = observation.LogBytesDelta is { } delta
            ? $"log delta {delta.ToString("N0", CultureInfo.InvariantCulture)} B"
            : "log delta unknown";

        switch (observed)
        {
            case ObservedMovement.Inconclusive:
                unknowns.Add($"movement: {logDetail} falls between the metadata-only and rewrite bands");
                break;
            case ObservedMovement.MetadataOnly when row.Movement == DataMovement.MetadataOnly:
                matches.Add($"movement: {logDetail} confirms metadata_only");
                break;
            case ObservedMovement.MetadataOnly when row.Movement == DataMovement.Rewrite:
                mismatches.Add($"movement: catalog says rewrite but {logDetail} indicates metadata-only");
                break;
            case ObservedMovement.Rewrite when row.Movement == DataMovement.Rewrite:
                matches.Add($"movement: {logDetail} confirms rewrite");
                break;
            case ObservedMovement.Rewrite when row.Movement == DataMovement.MetadataOnly:
                mismatches.Add($"movement: catalog says metadata_only but {logDetail} indicates a rewrite");
                break;
            default:
                // full_scan, index_build, fails_if_rows, deallocate, none: a log delta alone
                // cannot confirm or deny these — such probes must add reads or error evidence.
                unknowns.Add($"movement: catalog class '{row.Movement}' needs evidence beyond log bytes ({logDetail})");
                break;
        }
    }

    private static void EvaluateBlocking(
        DdlBehavior row, ProbeObservation observation,
        List<string> matches, List<string> mismatches, List<string> unknowns)
    {
        if (observation.Blocking is not { } measured)
        {
            unknowns.Add("blocking: declared by the probe but not observed");
            return;
        }

        var expected = ExpectedBlockingProfile(row.Lock);
        var detail = $"measured readsBlocked={measured.ReadsBlocked}, writesBlocked={measured.WritesBlocked}";
        if (measured == expected)
        {
            matches.Add($"blocking: {detail} matches catalog lock '{row.Lock}'");
        }
        else
        {
            mismatches.Add(
                $"blocking: catalog lock '{row.Lock}' implies readsBlocked={expected.ReadsBlocked}, " +
                $"writesBlocked={expected.WritesBlocked} but {detail}");
        }
    }

    private static void EvaluateError(
        ProbeExpectation expectation, ProbeObservation observation,
        List<string> matches, List<string> mismatches, List<string> unknowns)
    {
        if (expectation.ErrorNumber is not { } expectedNumber)
        {
            unknowns.Add("error: aspect declared without an expected error number");
            return;
        }

        if (observation.Error is not { } error)
        {
            unknowns.Add("error: declared by the probe but not observed");
            return;
        }

        if (error.Number == expectedNumber)
        {
            matches.Add($"error: statement failed with {expectedNumber} as cataloged");
        }
        else if (error.Number is null)
        {
            mismatches.Add($"error: expected {expectedNumber} but the statement succeeded");
        }
        else
        {
            mismatches.Add($"error: expected {expectedNumber} but got {error.Number}");
        }
    }
}
