using Planizer.MsSql;

namespace Planizer.CatalogVerification.Tests;

/// <summary>Verdict evaluation against synthetic catalog rows; no server involved.</summary>
public sealed class EvaluatorTests
{
    private static readonly DdlBehavior MetadataRow =
        new(LockLevel.SchMBrief, DataMovement.MetadataOnly, Reversibility.Yes, null);

    private static readonly DdlBehavior RewriteRow =
        new(LockLevel.SchM, DataMovement.Rewrite, Reversibility.Yes, null);

    private static readonly DdlBehavior FullScanRow =
        new(LockLevel.SchM, DataMovement.FullScan, Reversibility.Yes, null);

    private static readonly DdlBehavior STableRow =
        new(LockLevel.STable, DataMovement.IndexBuild, Reversibility.Yes, null);

    private static readonly ProbeExpectation MovementOnly = new(ProbeAspects.Movement);
    private static readonly ProbeExpectation BlockingOnly = new(ProbeAspects.Blocking);

    private static ProbeObservation Moved(ObservedMovement movement, long logBytes) =>
        new() { Movement = movement, LogBytesDelta = logBytes };

    [Fact]
    public void Observed_metadata_verifies_a_metadata_row()
    {
        var verdict = VerdictEvaluator.Evaluate(MetadataRow, MovementOnly, Moved(ObservedMovement.MetadataOnly, 412));
        Assert.Equal(VerdictKind.Verified, verdict.Kind);
        Assert.Contains("metadata_only", verdict.Evidence);
    }

    [Fact]
    public void Observed_rewrite_contradicts_a_metadata_row()
    {
        var verdict = VerdictEvaluator.Evaluate(MetadataRow, MovementOnly, Moved(ObservedMovement.Rewrite, 5_000_000));
        Assert.Equal(VerdictKind.Contradicted, verdict.Kind);
        Assert.Contains("catalog says metadata_only", verdict.Evidence);
    }

    [Fact]
    public void Observed_metadata_contradicts_a_rewrite_row()
    {
        var verdict = VerdictEvaluator.Evaluate(RewriteRow, MovementOnly, Moved(ObservedMovement.MetadataOnly, 412));
        Assert.Equal(VerdictKind.Contradicted, verdict.Kind);
        Assert.Contains("catalog says rewrite", verdict.Evidence);
    }

    [Fact]
    public void Inconclusive_movement_yields_an_inconclusive_verdict()
    {
        var verdict = VerdictEvaluator.Evaluate(MetadataRow, MovementOnly, Moved(ObservedMovement.Inconclusive, 100_000));
        Assert.Equal(VerdictKind.Inconclusive, verdict.Kind);
    }

    [Fact]
    public void Movement_declared_but_not_observed_is_inconclusive()
    {
        var verdict = VerdictEvaluator.Evaluate(MetadataRow, MovementOnly, new ProbeObservation());
        Assert.Equal(VerdictKind.Inconclusive, verdict.Kind);
        Assert.Contains("not observed", verdict.Evidence);
    }

    [Fact]
    public void Log_evidence_alone_cannot_judge_a_full_scan_row()
    {
        // A validation scan writes almost no log, so a metadata-only log classification must
        // not count as confirmation (or contradiction) of full_scan.
        var verdict = VerdictEvaluator.Evaluate(FullScanRow, MovementOnly, Moved(ObservedMovement.MetadataOnly, 412));
        Assert.Equal(VerdictKind.Inconclusive, verdict.Kind);
        Assert.Contains("beyond log bytes", verdict.Evidence);
    }

    [Fact]
    public void Reads_allowed_writes_blocked_verifies_an_s_table_row()
    {
        var observation = new ProbeObservation { Blocking = new BlockingProfile(false, true) };
        var verdict = VerdictEvaluator.Evaluate(STableRow, BlockingOnly, observation);
        Assert.Equal(VerdictKind.Verified, verdict.Kind);
    }

    [Fact]
    public void Fully_blocking_profile_contradicts_an_s_table_row()
    {
        var observation = new ProbeObservation { Blocking = new BlockingProfile(true, true) };
        var verdict = VerdictEvaluator.Evaluate(STableRow, BlockingOnly, observation);
        Assert.Equal(VerdictKind.Contradicted, verdict.Kind);
        Assert.Contains("readsBlocked=False", verdict.Evidence);
    }

    [Fact]
    public void Blocking_declared_but_not_observed_is_inconclusive()
    {
        var verdict = VerdictEvaluator.Evaluate(STableRow, BlockingOnly, new ProbeObservation());
        Assert.Equal(VerdictKind.Inconclusive, verdict.Kind);
    }

    [Fact]
    public void Matching_error_number_verifies()
    {
        var expectation = new ProbeExpectation(ProbeAspects.Error, 4901);
        var observation = new ProbeObservation { Error = new ErrorObservation(4901) };
        Assert.Equal(VerdictKind.Verified, VerdictEvaluator.Evaluate(MetadataRow, expectation, observation).Kind);
    }

    [Fact]
    public void Succeeding_statement_contradicts_an_expected_error()
    {
        var expectation = new ProbeExpectation(ProbeAspects.Error, 4901);
        var observation = new ProbeObservation { Error = new ErrorObservation(null) };
        var verdict = VerdictEvaluator.Evaluate(MetadataRow, expectation, observation);
        Assert.Equal(VerdictKind.Contradicted, verdict.Kind);
        Assert.Contains("succeeded", verdict.Evidence);
    }

    [Fact]
    public void Different_error_number_contradicts()
    {
        var expectation = new ProbeExpectation(ProbeAspects.Error, 4901);
        var observation = new ProbeObservation { Error = new ErrorObservation(515) };
        var verdict = VerdictEvaluator.Evaluate(MetadataRow, expectation, observation);
        Assert.Equal(VerdictKind.Contradicted, verdict.Kind);
        Assert.Contains("got 515", verdict.Evidence);
    }

    [Fact]
    public void Error_aspect_without_expected_number_is_inconclusive()
    {
        var expectation = new ProbeExpectation(ProbeAspects.Error);
        var observation = new ProbeObservation { Error = new ErrorObservation(4901) };
        Assert.Equal(VerdictKind.Inconclusive, VerdictEvaluator.Evaluate(MetadataRow, expectation, observation).Kind);
    }

    [Fact]
    public void A_single_mismatch_beats_any_number_of_matches()
    {
        var expectation = new ProbeExpectation(ProbeAspects.Movement | ProbeAspects.Blocking);
        var observation = new ProbeObservation
        {
            Movement = ObservedMovement.MetadataOnly,
            LogBytesDelta = 412,
            Blocking = new BlockingProfile(true, true),
        };

        // Movement matches the row, but blocking (reads blocked) contradicts its s_table lock.
        var mixedRow = new DdlBehavior(LockLevel.STable, DataMovement.MetadataOnly, Reversibility.Yes, null);
        var verdict = VerdictEvaluator.Evaluate(mixedRow, expectation, observation);
        Assert.Equal(VerdictKind.Contradicted, verdict.Kind);
    }

    [Fact]
    public void No_declared_aspect_is_inconclusive()
    {
        var verdict = VerdictEvaluator.Evaluate(MetadataRow, new ProbeExpectation(ProbeAspects.None), new ProbeObservation());
        Assert.Equal(VerdictKind.Inconclusive, verdict.Kind);
    }
}
