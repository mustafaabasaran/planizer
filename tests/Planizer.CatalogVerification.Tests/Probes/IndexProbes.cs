using Planizer.MsSql;

namespace Planizer.CatalogVerification.Tests.Probes;

/// <summary>
/// Catalog row <c>create_nonclustered_index_offline</c> (any edition): <c>s_table</c> /
/// <c>index_build</c> — reads allowed, writes blocked during the build. This probe settles the
/// documentation dispute recorded in <c>docs/rules/MSSQL-LOCK-002.md</c> empirically: while one
/// session holds the offline nonclustered CREATE INDEX open in a transaction, a second
/// session's SELECT must succeed and its UPDATE must time out, i.e. the expected blocking
/// profile is (readsBlocked=false, writesBlocked=true).
/// </summary>
public sealed class CreateNonclusteredIndexOfflineProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.CreateNonclusteredIndexOffline;

    public override ProbeExpectation Expectation => new(ProbeAspects.Blocking);

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        var profile = await Measurement.BlockingProfileAsync(
            session.OpenConnectionAsync,
            ddlSql: $"CREATE INDEX [IX_{TableName}_payload] ON {QualifiedTableName} (payload);",
            readProbeSql: $"SELECT TOP (1) id FROM {QualifiedTableName};",
            // Self-assignment: side-effect free even though it autocommits when not blocked.
            writeProbeSql: $"UPDATE TOP (1) {QualifiedTableName} SET payload = payload;");
        return new ProbeObservation { Blocking = profile };
    }
}
