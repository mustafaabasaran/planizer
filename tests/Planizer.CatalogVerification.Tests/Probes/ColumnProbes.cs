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

/// <summary>
/// Catalog row <c>add_column_notnull_default_nondet</c> (any edition): <c>schm</c> /
/// <c>rewrite</c>. A per-row default (NEWID) breaks the metadata-only fast path on every
/// edition — unlike the statement-level runtime constants of
/// <see cref="AddColumnNotNullDefaultConstProbe"/> — so the same rewrite-scale log delta must
/// show up on Developer and Express alike.
/// </summary>
public sealed class AddColumnNotNullDefaultNondetProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.AddColumnNotNullDefaultNondet;

    public override ProbeExpectation Expectation => new(ProbeAspects.Movement);

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        var measurement = await Measurement.MeasureInRolledBackTransactionAsync(
            connection,
            $"ALTER TABLE {QualifiedTableName} ADD guid_column uniqueidentifier NOT NULL " +
            $"CONSTRAINT [DF_{TableName}_guid] DEFAULT NEWID();");
        return ProbeObservation.FromMeasurement(measurement, RowCount);
    }
}

/// <summary>
/// Catalog row <c>add_column_notnull_no_default</c> (any edition): <c>fails_if_rows</c>.
/// On a populated table the statement must fail with error 4901 ("ALTER TABLE only allows
/// columns to be added that can contain nulls, or have a DEFAULT definition specified …"),
/// exactly the number the plan states for this row.
/// </summary>
public sealed class AddColumnNotNullNoDefaultProbe : CatalogProbeBase
{
    /// <summary>SQL Server error 4901: adding a NOT NULL column without DEFAULT to a non-empty table.</summary>
    public const int AlterTableAddRequiresDefaultErrorNumber = 4901;

    public override string OperationKey => DdlOperationKeys.AddColumnNotNullNoDefault;

    public override ProbeExpectation Expectation =>
        new(ProbeAspects.Error, AlterTableAddRequiresDefaultErrorNumber);

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        var errorNumber = await Measurement.ErrorNumberOfAsync(
            connection, $"ALTER TABLE {QualifiedTableName} ADD required_column int NOT NULL;");
        return new ProbeObservation { Error = new ErrorObservation(errorNumber) };
    }
}

/// <summary>
/// Catalog row <c>alter_column_widen_varlen</c> (any edition): <c>schm_brief</c> /
/// <c>metadata_only</c>. Widening <c>varchar(100)</c> to <c>varchar(200)</c> must not touch
/// row data on any edition.
/// </summary>
public sealed class AlterColumnWidenVarlenProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.AlterColumnWidenVarLen;

    public override ProbeExpectation Expectation => new(ProbeAspects.Movement);

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        var measurement = await Measurement.MeasureInRolledBackTransactionAsync(
            connection, $"ALTER TABLE {QualifiedTableName} ALTER COLUMN payload varchar(200) NOT NULL;");
        return ProbeObservation.FromMeasurement(measurement, RowCount);
    }
}

/// <summary>
/// Catalog row <c>alter_column_widen_to_max</c> (any edition): <c>schm</c> / <c>rewrite</c>.
/// Unlike an in-family widening, <c>varchar(n)</c> to <c>varchar(max)</c> changes the storage
/// class and is size-of-data: the log delta must land in the rewrite band.
/// </summary>
public sealed class AlterColumnWidenToMaxProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.AlterColumnWidenToMax;

    public override ProbeExpectation Expectation => new(ProbeAspects.Movement);

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        var measurement = await Measurement.MeasureInRolledBackTransactionAsync(
            connection, $"ALTER TABLE {QualifiedTableName} ALTER COLUMN payload varchar(max) NOT NULL;");
        return ProbeObservation.FromMeasurement(measurement, RowCount);
    }
}

/// <summary>
/// Catalog row <c>alter_column_fixed_len_change</c> (any edition): <c>schm</c> /
/// <c>rewrite</c>. Arrange adds a nullable <c>int</c> column and populates every row (a
/// populated column keeps the measurement free of the Enterprise metadata-default fast path
/// and of default-constraint dependency errors); the act then changes it to <c>bigint</c>,
/// which must rewrite every row.
/// </summary>
public sealed class AlterColumnFixedLenChangeProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.AlterColumnFixedLenChange;

    public override ProbeExpectation Expectation => new(ProbeAspects.Movement);

    public override async Task ArrangeAsync(ProbeSession session)
    {
        await base.ArrangeAsync(session);
        await using var connection = await session.OpenConnectionAsync();
        await Measurement.ExecuteAsync(
            connection,
            $"ALTER TABLE {QualifiedTableName} ADD fixed_width int NULL;",
            Measurement.LongCommandTimeoutSeconds);
        await Measurement.ExecuteAsync(
            connection,
            $"UPDATE {QualifiedTableName} SET fixed_width = id;",
            Measurement.LongCommandTimeoutSeconds);
    }

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        var measurement = await Measurement.MeasureInRolledBackTransactionAsync(
            connection, $"ALTER TABLE {QualifiedTableName} ALTER COLUMN fixed_width bigint NULL;");
        return ProbeObservation.FromMeasurement(measurement, RowCount);
    }
}

/// <summary>
/// Catalog row <c>alter_column_narrow</c> (any edition): <c>schm</c> / <c>rewrite</c>. The
/// probe measures the branch the plan pins down — every value fits (the arranged payload is at
/// most 10 characters), so narrowing <c>varchar(100)</c> to <c>varchar(50)</c> is expected to
/// rewrite. The does-not-fit branch is deliberately not asserted: its error number is
/// uncertain in the plan (truncation reports 2628 under compatibility level 150+ and 8152
/// before; 8115 is the numeric-overflow sibling), so no number is guessed here.
/// </summary>
public sealed class AlterColumnNarrowProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.AlterColumnNarrow;

    public override ProbeExpectation Expectation => new(ProbeAspects.Movement);

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        var measurement = await Measurement.MeasureInRolledBackTransactionAsync(
            connection, $"ALTER TABLE {QualifiedTableName} ALTER COLUMN payload varchar(50) NOT NULL;");
        return ProbeObservation.FromMeasurement(measurement, RowCount);
    }
}

/// <summary>
/// Catalog row <c>alter_column_null_to_notnull</c> (any edition): <c>schm</c> /
/// <c>full_scan</c>. A log delta cannot confirm a validation scan, so this probe verifies the
/// lock column instead: while the ALTER is held open, a second session must be unable to read
/// or write (Sch-M profile). Arrange populates the nullable column so the scan finds no NULLs.
/// Two follow-ups stay out of scope by design: confirming the scan itself needs reads-based
/// evidence in the evaluator, and the NULLs-present failure number (515 vs 4901 — the plan
/// leaves it to be tested) is not guessed here.
/// </summary>
public sealed class AlterColumnNullToNotNullProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.AlterColumnNullToNotNull;

    public override ProbeExpectation Expectation => new(ProbeAspects.Blocking);

    public override async Task ArrangeAsync(ProbeSession session)
    {
        await base.ArrangeAsync(session);
        await using var connection = await session.OpenConnectionAsync();
        await Measurement.ExecuteAsync(
            connection,
            $"ALTER TABLE {QualifiedTableName} ADD maybe_null int NULL;",
            Measurement.LongCommandTimeoutSeconds);
        await Measurement.ExecuteAsync(
            connection,
            $"UPDATE {QualifiedTableName} SET maybe_null = id;",
            Measurement.LongCommandTimeoutSeconds);
    }

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        var profile = await Measurement.BlockingProfileAsync(
            session.OpenConnectionAsync,
            ddlSql: $"ALTER TABLE {QualifiedTableName} ALTER COLUMN maybe_null int NOT NULL;",
            readProbeSql: $"SELECT TOP (1) id FROM {QualifiedTableName};",
            // Self-assignment: side-effect free even though it autocommits when not blocked.
            writeProbeSql: $"UPDATE TOP (1) {QualifiedTableName} SET payload = payload;");
        return new ProbeObservation { Blocking = profile };
    }
}

/// <summary>
/// Catalog row <c>alter_column_notnull_to_null</c> (any edition): <c>schm_brief</c> /
/// <c>metadata_only</c>. Relaxing NOT NULL must not touch row data.
/// </summary>
public sealed class AlterColumnNotNullToNullProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.AlterColumnNotNullToNull;

    public override ProbeExpectation Expectation => new(ProbeAspects.Movement);

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        var measurement = await Measurement.MeasureInRolledBackTransactionAsync(
            connection, $"ALTER TABLE {QualifiedTableName} ALTER COLUMN payload varchar(100) NULL;");
        return ProbeObservation.FromMeasurement(measurement, RowCount);
    }
}

/// <summary>
/// Catalog row <c>alter_column_collation</c> (any edition): <c>schm</c> / <c>rewrite</c>. The
/// target collation (<c>Latin1_General_100_CI_AS</c>) differs from the container default
/// (<c>SQL_Latin1_General_CP1_CI_AS</c>) but shares its code page (1252), so the swap writes
/// metadata only — the first CI run measured ~0.5 KB of log and corrected the catalog row from
/// "rewrite" to "metadata_only". The size-of-data branch (varchar crossing code pages) and the
/// dependent-index restriction remain documented on MSSQL-RW-009.
/// </summary>
public sealed class AlterColumnCollationProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.AlterColumnCollation;

    public override ProbeExpectation Expectation => new(ProbeAspects.Movement);

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        var measurement = await Measurement.MeasureInRolledBackTransactionAsync(
            connection,
            $"ALTER TABLE {QualifiedTableName} ALTER COLUMN payload varchar(100) " +
            "COLLATE Latin1_General_100_CI_AS NOT NULL;");
        return ProbeObservation.FromMeasurement(measurement, RowCount);
    }
}

/// <summary>
/// Catalog row <c>drop_column</c> (any edition): <c>schm_brief</c> / <c>metadata_only</c>.
/// Dropping a column must write metadata-level log only (the space stays unreclaimed, which a
/// log delta cannot see and the catalog notes separately).
/// </summary>
public sealed class DropColumnProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.DropColumn;

    public override ProbeExpectation Expectation => new(ProbeAspects.Movement);

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        var measurement = await Measurement.MeasureInRolledBackTransactionAsync(
            connection, $"ALTER TABLE {QualifiedTableName} DROP COLUMN payload;");
        return ProbeObservation.FromMeasurement(measurement, RowCount);
    }
}

/// <summary>
/// Catalog row <c>add_computed_persisted</c> (any edition): <c>schm</c> / <c>full_scan</c>.
/// As with <see cref="AlterColumnNullToNotNullProbe"/>, the movement class cannot be judged
/// from log bytes (a persisted add also materializes values, so the log evidence is
/// ambiguous by design of the classifier); the probe therefore verifies the lock column: the
/// held-open ALTER must block both reads and writes (Sch-M profile).
/// </summary>
public sealed class AddComputedPersistedProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.AddComputedPersisted;

    public override ProbeExpectation Expectation => new(ProbeAspects.Blocking);

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        var profile = await Measurement.BlockingProfileAsync(
            session.OpenConnectionAsync,
            ddlSql: $"ALTER TABLE {QualifiedTableName} ADD doubled_id AS (id * 2) PERSISTED;",
            readProbeSql: $"SELECT TOP (1) id FROM {QualifiedTableName};",
            // Self-assignment: side-effect free even though it autocommits when not blocked.
            writeProbeSql: $"UPDATE TOP (1) {QualifiedTableName} SET payload = payload;");
        return new ProbeObservation { Blocking = profile };
    }
}

/// <summary>
/// Catalog rows <c>data_compression_change</c>: <c>schm</c> / <c>rewrite</c> on Enterprise and
/// (since 2016 SP1) on Standard/Express — the CI image is 2022, so the probe runs on both
/// matrix editions. The offline rebuild with PAGE compression rebuilds the whole rowset; note
/// that a rebuild can be minimally logged, in which case the delta lands between the bands and
/// the verdict degrades to Inconclusive rather than contradicting the catalog.
/// </summary>
public sealed class DataCompressionChangeProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.DataCompressionChange;

    public override ProbeExpectation Expectation => new(ProbeAspects.Movement);

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        var measurement = await Measurement.MeasureInRolledBackTransactionAsync(
            connection,
            $"ALTER TABLE {QualifiedTableName} REBUILD PARTITION = ALL WITH (DATA_COMPRESSION = PAGE);");
        return ProbeObservation.FromMeasurement(measurement, RowCount);
    }
}

/// <summary>
/// Catalog row <c>add_default_constraint</c> (any edition): <c>schm_brief</c> /
/// <c>metadata_only</c>. Adding a DEFAULT for an existing column must not touch existing rows.
/// </summary>
public sealed class AddDefaultConstraintProbe : CatalogProbeBase
{
    public override string OperationKey => DdlOperationKeys.AddDefaultConstraint;

    public override ProbeExpectation Expectation => new(ProbeAspects.Movement);

    public override async Task<ProbeObservation> ActAsync(ProbeSession session)
    {
        await using var connection = await session.OpenConnectionAsync();
        var measurement = await Measurement.MeasureInRolledBackTransactionAsync(
            connection,
            $"ALTER TABLE {QualifiedTableName} ADD CONSTRAINT [DF_{TableName}_payload] " +
            "DEFAULT ('') FOR payload;");
        return ProbeObservation.FromMeasurement(measurement, RowCount);
    }
}
