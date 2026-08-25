using Planizer.MsSql;

namespace Planizer.CatalogVerification.Tests.Probes;

/// <summary>
/// Catalog row <c>add_column_nullable</c> (any edition): <c>schm_brief</c> / <c>metadata_only</c>.
/// Adding a nullable column must write metadata-level log only, on every edition.
/// </summary>
public sealed class AddColumnNullableProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.AddColumnNullable;

    public override ProbeExpectation Expectation => new(ProbeAspects.Movement);

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        var measurement = await Measurement.MeasureInRolledBackTransactionAsync(
            connection, $"ALTER TABLE {QualifiedTableName} ADD extra_column int NULL;");
        return ProbeObservation.FromMeasurement(measurement, RowCount);
    }
}

/// <summary>
/// Catalog rows <c>add_column_notnull_default_const</c>: NOT NULL plus a runtime-constant
/// DEFAULT is <c>metadata_only</c> on Enterprise (Developer) but a full <c>rewrite</c> on
/// Standard/Express. The catalog lookup resolves the edition-specific row, so the CI matrix
/// (Developer and Express PIDs) asserts the edition difference itself: the same probe must
/// classify metadata-only on Developer and rewrite on Express.
/// </summary>
public sealed class AddColumnNotNullDefaultConstProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.AddColumnNotNullDefaultConst;

    public override ProbeExpectation Expectation => new(ProbeAspects.Movement);

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        var measurement = await Measurement.MeasureInRolledBackTransactionAsync(
            connection,
            $"ALTER TABLE {QualifiedTableName} ADD filled_column int NOT NULL " +
            $"CONSTRAINT [DF_{TableName}_filled] DEFAULT (42);");
        return ProbeObservation.FromMeasurement(measurement, RowCount);
    }
}
