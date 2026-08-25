using Microsoft.Data.SqlClient;
using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.CatalogVerification.Tests.Probes;

/// <summary>Engine error numbers the index probes assert on.</summary>
public static class IndexProbeErrors
{
    /// <summary>
    /// Error 1712: online index operations can only be performed in Enterprise edition.
    /// Raised when <c>ONLINE = ON</c> is attempted on Express/Standard (rule MSSQL-LOCK-003).
    /// </summary>
    public const int OnlineOperationsRequireEnterprise = 1712;

    /// <summary>
    /// Error 574: the statement cannot be used inside a user transaction. Raised for
    /// <c>RESUMABLE = ON</c> index operations inside an explicit transaction (the restriction
    /// behind the MSSQL-LOCK-005 correction).
    /// </summary>
    public const int StatementNotAllowedInUserTransaction = 574;
}

/// <summary>
/// Base for index probes whose act is a two-session blocking profile against the probe table:
/// shared read/write probe statements plus the standard measurement call.
/// </summary>
public abstract class IndexBlockingProbeBase : CatalogProbeBase
{
    protected string ReadProbeSql => $"SELECT TOP (1) id FROM {QualifiedTableName};";

    /// <summary>Self-assignment: side-effect free even though it autocommits when not blocked.</summary>
    protected string WriteProbeSql => $"UPDATE TOP (1) {QualifiedTableName} SET payload = payload;";

    protected async Task<ProbeObservation> MeasureBlockingAsync(ProbeSession session, string ddlSql)
    {
        var profile = await Measurement.BlockingProfileAsync(
            session.OpenConnectionAsync, ddlSql, ReadProbeSql, WriteProbeSql);
        return new ProbeObservation { Blocking = profile };
    }

    /// <summary>
    /// For rows whose expected profile leaves reads unblocked (<c>s_table</c>, <c>s_brief</c>,
    /// <c>none</c>): the held-open technique would retain the DDL's brief metadata locks and
    /// wrongly block the read probe, so these sample WHILE the DDL runs. A missed window yields
    /// a null profile, which the evaluator reports as Inconclusive.
    /// </summary>
    protected async Task<ProbeObservation> MeasureConcurrentBlockingAsync(ProbeSession session, string ddlSql)
    {
        var profile = await Measurement.ConcurrentBlockingProfileAsync(
            session.OpenConnectionAsync, ddlSql, ReadProbeSql, WriteProbeSql);
        return new ProbeObservation { Blocking = profile, SampledDuringExecution = true };
    }
}

/// <summary>
/// Base for index probes that need a heap (no clustered index, no primary key) instead of the
/// standard probe table, so their act can create or add the clustered structure itself.
/// </summary>
public abstract class HeapTableIndexProbeBase : IndexBlockingProbeBase
{
    public override async Task ArrangeAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        await Measurement.ExecuteAsync(connection, ProbeSql.DropTable(TableName));
        await Measurement.ExecuteAsync(
            connection,
            $"""
            CREATE TABLE dbo.[{TableName}] (
                id int NOT NULL,
                payload varchar(100) NOT NULL);
            INSERT INTO dbo.[{TableName}] (id, payload)
            SELECT value, CONCAT('row-', value)
            FROM GENERATE_SERIES(1, {RowCount});
            """,
            Measurement.LongCommandTimeoutSeconds);
    }
}

/// <summary>
/// Catalog row <c>create_nonclustered_index_offline</c> (any edition): <c>s_table</c> /
/// <c>index_build</c> — reads allowed, writes blocked during the build. This probe settles the
/// documentation dispute recorded in <c>docs/rules/MSSQL-LOCK-002.md</c> empirically: while one
/// session holds the offline nonclustered CREATE INDEX open in a transaction, a second
/// session's SELECT must succeed and its UPDATE must time out, i.e. the expected blocking
/// profile is (readsBlocked=false, writesBlocked=true).
/// </summary>
public sealed class CreateNonclusteredIndexOfflineProbe : IndexBlockingProbeBase
{
    public override string OperationKey => DdlOperationKeys.CreateNonclusteredIndexOffline;

    public override ProbeExpectation Expectation => new(ProbeAspects.Blocking);

    // First CI run finished the 500k build inside the sampling gap; two million rows keep the
    // offline build observable from the prober.
    protected override int RowCount => 2_000_000;

    public override Task<ProbeObservation> ActAsync(ProbeSession session) =>
        MeasureConcurrentBlockingAsync(
            session,
            $"CREATE INDEX [IX_{TableName}_payload] ON {QualifiedTableName} (payload);");
}

/// <summary>
/// Catalog row <c>create_nonclustered_index_online</c> (enterprise only): <c>s_brief</c> /
/// <c>index_build</c> — per the verified semantics of <c>docs/rules/MSSQL-LOCK-004.md</c>, an
/// online nonclustered build never takes a blocking table Sch-M; its preparation and final
/// phases each take a brief shared (S) table lock. Held open by the probe's transaction, that
/// S lock must block writes but not reads: expected profile (readsBlocked=false,
/// writesBlocked=true). Applies only where the enterprise-scoped catalog row resolves; the
/// Express side of the edition gate (error 1712) is verified by
/// <see cref="OnlineIndexEditionGateTests"/>. Should the engine refuse the online build inside
/// a user transaction, the probe crashes into an Inconclusive verdict by design.
/// </summary>
public sealed class CreateNonclusteredIndexOnlineProbe : IndexBlockingProbeBase
{
    public override string OperationKey => DdlOperationKeys.CreateNonclusteredIndexOnline;

    public override ProbeExpectation Expectation => new(ProbeAspects.Blocking);

    public override bool AppliesTo(SqlEdition edition) => edition == SqlEdition.Enterprise;

    // A wide window: half a million rows keep the online build observable from the prober.
    protected override int RowCount => 500_000;

    public override Task<ProbeObservation> ActAsync(ProbeSession session) =>
        MeasureConcurrentBlockingAsync(
            session,
            $"CREATE INDEX [IX_{TableName}_payload] ON {QualifiedTableName} (payload) WITH (ONLINE = ON);");
}

/// <summary>
/// Catalog row <c>create_clustered_index_on_heap</c> (any edition): <c>schm</c> /
/// <c>rewrite</c>. An offline clustered build takes a schema-modification (Sch-M) lock — all
/// access blocked (undisputed per <c>docs/rules/MSSQL-LOCK-002.md</c>): expected profile
/// (readsBlocked=true, writesBlocked=true). Movement is not declared: the log-delta bands
/// cannot separate an index build from a rewrite under an unknown recovery model.
/// </summary>
public sealed class CreateClusteredIndexOnHeapProbe : HeapTableIndexProbeBase
{
    public override string OperationKey => DdlOperationKeys.CreateClusteredIndexOnHeap;

    public override ProbeExpectation Expectation => new(ProbeAspects.Blocking);

    public override Task<ProbeObservation> ActAsync(ProbeSession session) =>
        MeasureBlockingAsync(session, $"CREATE CLUSTERED INDEX [CX_{TableName}] ON {QualifiedTableName} (id);");
}

/// <summary>
/// Catalog row <c>create_clustered_index_online</c> (enterprise only): <c>schm_brief</c> /
/// <c>index_build</c> — per <c>docs/rules/MSSQL-LOCK-004.md</c>, an online clustered create
/// starts on a shared (S) lock and completes on a brief final-phase Sch-M. Held open by the
/// probe's transaction the retained Sch-M blocks everything: expected profile
/// (readsBlocked=true, writesBlocked=true). The held-open technique verifies the lock
/// category, never the "brief" duration. A refusal to run inside a user transaction degrades
/// to Inconclusive by design.
/// </summary>
public sealed class CreateClusteredIndexOnlineProbe : HeapTableIndexProbeBase
{
    public override string OperationKey => DdlOperationKeys.CreateClusteredIndexOnline;

    public override ProbeExpectation Expectation => new(ProbeAspects.Blocking);

    public override bool AppliesTo(SqlEdition edition) => edition == SqlEdition.Enterprise;

    public override Task<ProbeObservation> ActAsync(ProbeSession session) =>
        MeasureBlockingAsync(
            session,
            $"CREATE CLUSTERED INDEX [CX_{TableName}] ON {QualifiedTableName} (id) WITH (ONLINE = ON);");
}

/// <summary>
/// Catalog row <c>drop_clustered_index</c> (any edition): <c>schm</c> / <c>rewrite</c>.
/// Dropping the clustered index turns the table back into a heap under a schema-modification
/// lock: expected profile (readsBlocked=true, writesBlocked=true). The arrange builds a
/// non-constraint clustered index so plain <c>DROP INDEX</c> can remove it.
/// </summary>
public sealed class DropClusteredIndexProbe : HeapTableIndexProbeBase
{
    public override string OperationKey => DdlOperationKeys.DropClusteredIndex;

    public override ProbeExpectation Expectation => new(ProbeAspects.Blocking);

    public override async Task ArrangeAsync(ProbeSession session)
    {
        await base.ArrangeAsync(session);
        await using var connection = await session.OpenConnectionAsync();
        await Measurement.ExecuteAsync(
            connection,
            $"CREATE CLUSTERED INDEX [CX_{TableName}] ON {QualifiedTableName} (id);",
            Measurement.LongCommandTimeoutSeconds);
    }

    public override Task<ProbeObservation> ActAsync(ProbeSession session) =>
        MeasureBlockingAsync(session, $"DROP INDEX [CX_{TableName}] ON {QualifiedTableName};");
}

/// <summary>
/// Catalog row <c>alter_index_rebuild_offline</c> (any edition): <c>schm</c> /
/// <c>index_build</c>. An offline rebuild of the clustered primary key runs under a
/// schema-modification lock: expected profile (readsBlocked=true, writesBlocked=true).
/// </summary>
public sealed class AlterIndexRebuildOfflineProbe : IndexBlockingProbeBase
{
    public override string OperationKey => DdlOperationKeys.AlterIndexRebuildOffline;

    public override ProbeExpectation Expectation => new(ProbeAspects.Blocking);

    public override Task<ProbeObservation> ActAsync(ProbeSession session) =>
        MeasureBlockingAsync(session, $"ALTER INDEX [PK_{TableName}] ON {QualifiedTableName} REBUILD;");
}

/// <summary>
/// Catalog row <c>alter_index_rebuild_online</c> (enterprise only): <c>schm_brief</c> /
/// <c>index_build</c> — per <c>docs/rules/MSSQL-LOCK-004.md</c> every online rebuild starts on
/// a shared (S) lock and completes on a brief final-phase Sch-M; held open by the probe's
/// transaction that Sch-M blocks everything: expected profile (readsBlocked=true,
/// writesBlocked=true). The probe additionally verifies the restriction behind the
/// MSSQL-LOCK-005 correction: <c>RESUMABLE = ON</c> inside an explicit transaction must fail
/// with error 574. The error is measured first so that evidence survives even if the engine
/// refuses the plain online rebuild inside a user transaction — that refusal is swallowed into
/// an unobserved blocking aspect, degrading the verdict to Inconclusive rather than crashing.
/// </summary>
public sealed class AlterIndexRebuildOnlineProbe : IndexBlockingProbeBase
{
    public override string OperationKey => DdlOperationKeys.AlterIndexRebuildOnline;

    public override ProbeExpectation Expectation =>
        new(ProbeAspects.Blocking | ProbeAspects.Error, IndexProbeErrors.StatementNotAllowedInUserTransaction);

    public override bool AppliesTo(SqlEdition edition) => edition == SqlEdition.Enterprise;

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        // ErrorNumberOfAsync wraps the act in BEGIN TRAN … ROLLBACK — exactly the "inside an
        // explicit transaction" condition that RESUMABLE rejects with error 574.
        int? resumableError;
        await using (var connection = await session.OpenConnectionAsync())
        {
            resumableError = await Measurement.ErrorNumberOfAsync(
                connection,
                $"ALTER INDEX [PK_{TableName}] ON {QualifiedTableName} REBUILD WITH (ONLINE = ON, RESUMABLE = ON);");
        }

        BlockingProfile? blocking = null;
        try
        {
            blocking = await Measurement.BlockingProfileAsync(
                session.OpenConnectionAsync,
                $"ALTER INDEX [PK_{TableName}] ON {QualifiedTableName} REBUILD WITH (ONLINE = ON);",
                ReadProbeSql,
                WriteProbeSql);
        }
        catch (SqlException)
        {
            // Leave the blocking aspect unobserved: the evaluator turns it into an
            // Inconclusive verdict while the error 574 evidence above still counts.
        }

        return new ProbeObservation { Blocking = blocking, Error = new ErrorObservation(resumableError) };
    }
}

/// <summary>
/// Catalog row <c>alter_index_reorganize</c> (any edition): <c>none</c> / <c>index_build</c> —
/// reorganize is always online and takes no table lock of note: expected profile
/// (readsBlocked=false, writesBlocked=false). On the freshly built probe index the reorganize
/// is a near no-op, which is exactly what the profile needs. Should the engine refuse
/// REORGANIZE inside a user transaction, the probe crashes into an Inconclusive verdict by
/// design rather than guessing an error number the plan does not state.
/// </summary>
public sealed class AlterIndexReorganizeProbe : IndexBlockingProbeBase
{
    public override string OperationKey => DdlOperationKeys.AlterIndexReorganize;

    public override ProbeExpectation Expectation => new(ProbeAspects.Blocking);

    // An unfragmented index reorganizes near-instantly; a missed window reports Inconclusive
    // by design rather than pretending the always-online claim was verified.
    public override Task<ProbeObservation> ActAsync(ProbeSession session) =>
        MeasureConcurrentBlockingAsync(session, $"ALTER INDEX [PK_{TableName}] ON {QualifiedTableName} REORGANIZE;");
}

/// <summary>
/// Catalog row <c>add_pk_or_unique</c> (any edition): <c>schm</c> / <c>index_build</c>. Adding
/// a primary key to a heap builds the underlying unique index and validates uniqueness under a
/// schema-modification lock: expected profile (readsBlocked=true, writesBlocked=true).
/// </summary>
public sealed class AddPkOrUniqueProbe : HeapTableIndexProbeBase
{
    public override string OperationKey => DdlOperationKeys.AddPkOrUnique;

    public override ProbeExpectation Expectation => new(ProbeAspects.Blocking);

    public override Task<ProbeObservation> ActAsync(ProbeSession session) =>
        MeasureBlockingAsync(
            session,
            $"ALTER TABLE {QualifiedTableName} ADD CONSTRAINT [PK_{TableName}] PRIMARY KEY CLUSTERED (id);");
}

/// <summary>
/// The Express side of the online-index edition gate, which cannot be a catalog probe: the
/// <c>create_*_index_online</c> and <c>alter_index_rebuild_online</c> rows are deliberately
/// enterprise-scoped, so on Express the runner has no row to compare against — the absence of
/// a row IS the catalog's claim there. This gated fact verifies that claim empirically, per
/// the plan: <c>ONLINE = ON</c> on Express must fail with error 1712 (rule MSSQL-LOCK-003),
/// and the very same statement must succeed on Developer/Enterprise, which also backs the
/// enterprise-scoped rows the probes above measure. Locally the fact always skips.
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class OnlineIndexEditionGateTests
{
    /// <summary>Unique probe table name, outside the <c>probe_&lt;operation_key&gt;</c> family.</summary>
    public const string TableName = "probe_online_index_edition_gate";

    /// <summary>The gate is about edition, not size; a small table keeps the fact fast.</summary>
    private const int RowCount = 1_000;

    private readonly ServerFixture _server;

    public OnlineIndexEditionGateTests(ServerFixture server) => _server = server;

    [VerifyFact]
    public async Task Online_nonclustered_create_is_gated_to_enterprise_editions()
    {
        await using var connection = await _server.OpenConnectionAsync();
        await Measurement.ExecuteAsync(connection, ProbeSql.DropTable(TableName));
        await Measurement.ExecuteAsync(
            connection, ProbeSql.CreateFilledTable(TableName, RowCount), Measurement.LongCommandTimeoutSeconds);
        try
        {
            int? errorNumber = null;
            try
            {
                // Bare execution, no user transaction: the edition gate must not be muddied by
                // any "online operation inside a transaction" restriction.
                await Measurement.ExecuteAsync(
                    connection,
                    $"CREATE INDEX [IX_{TableName}_payload] ON dbo.[{TableName}] (payload) WITH (ONLINE = ON);",
                    Measurement.LongCommandTimeoutSeconds);
            }
            catch (SqlException exception)
            {
                errorNumber = exception.Number;
            }

            if (_server.Edition is SqlEdition.Enterprise or SqlEdition.Azure)
            {
                Assert.Null(errorNumber);
            }
            else
            {
                Assert.Equal(IndexProbeErrors.OnlineOperationsRequireEnterprise, errorNumber);
            }
        }
        finally
        {
            await Measurement.ExecuteAsync(connection, ProbeSql.DropTable(TableName));
        }
    }
}
