using Planizer.MsSql;

namespace Planizer.CatalogVerification.Tests;

/// <summary>Threshold tests for the pure classification helpers; no server involved.</summary>
public sealed class ClassificationTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(412L)]
    [InlineData(65_535L)]
    public void Log_delta_below_64KB_is_metadata_only(long delta) =>
        Assert.Equal(ObservedMovement.MetadataOnly, Measurement.ClassifyLogBytes(delta, Measurement.DefaultProbeRowCount));

    [Theory]
    [InlineData(800_001L)]
    [InlineData(5_000_000L)]
    public void Log_delta_above_8_bytes_per_row_is_rewrite(long delta) =>
        Assert.Equal(ObservedMovement.Rewrite, Measurement.ClassifyLogBytes(delta, Measurement.DefaultProbeRowCount));

    [Theory]
    [InlineData(65_536L)] // exactly 64 KB: not strictly below the ceiling
    [InlineData(100_000L)]
    [InlineData(800_000L)] // exactly 8 B/row: not strictly above the floor
    public void Log_delta_between_the_bands_is_inconclusive(long delta) =>
        Assert.Equal(ObservedMovement.Inconclusive, Measurement.ClassifyLogBytes(delta, Measurement.DefaultProbeRowCount));

    [Fact]
    public void Rewrite_floor_is_8_bytes_per_row()
    {
        Assert.Equal(800_000L, Measurement.RewriteLogFloorBytes(100_000));
        Assert.Equal(80_000L, Measurement.RewriteLogFloorBytes(10_000));
    }

    [Fact]
    public void Standard_probe_table_keeps_the_bands_disjoint() =>
        Assert.True(Measurement.RewriteLogFloorBytes(Measurement.DefaultProbeRowCount)
            > Measurement.MetadataOnlyLogCeilingBytes);

    // Sch-M blocks all access; a table S lock blocks writes but allows reads (MSSQL-LOCK-002);
    // an online build's brief S — retained while the probe holds the transaction open — behaves
    // the same, and never blocks reads the way Sch-M does (MSSQL-LOCK-004).
    [Theory]
    [InlineData(LockLevel.SchM, true, true)]
    [InlineData(LockLevel.SchMBrief, true, true)]
    [InlineData(LockLevel.STable, false, true)]
    [InlineData(LockLevel.SBrief, false, true)]
    [InlineData(LockLevel.None, false, false)]
    public void Lock_level_maps_to_blocking_profile(LockLevel lockLevel, bool readsBlocked, bool writesBlocked) =>
        Assert.Equal(
            new BlockingProfile(readsBlocked, writesBlocked),
            VerdictEvaluator.ExpectedBlockingProfile(lockLevel));
}
