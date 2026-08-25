using System.Text;
using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.CatalogVerification.Tests;

/// <summary>
/// Discovers every <see cref="ICatalogProbe"/> in this assembly by reflection, runs them
/// data-driven against the catalog (<see cref="DdlBehaviorCatalog.Load"/>), and turns the
/// outcomes into a markdown report. One crashing probe never stops the others, and only a
/// Contradicted verdict fails the job.
/// </summary>
public static class ProbeRunner
{
    /// <summary>All concrete probes in this assembly, in deterministic (type name) order.</summary>
    public static IReadOnlyList<ICatalogProbe> DiscoverProbes() =>
        typeof(ProbeRunner).Assembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(ICatalogProbe).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => (ICatalogProbe)Activator.CreateInstance(type)!)
            .ToList();

    public static async Task<IReadOnlyList<ProbeOutcome>> RunAllAsync(ProbeSession session)
    {
        var catalog = DdlBehaviorCatalog.Load();
        var outcomes = new List<ProbeOutcome>();
        foreach (var probe in DiscoverProbes())
        {
            outcomes.Add(await RunOneAsync(probe, catalog, session));
        }

        return outcomes;
    }

    /// <summary>
    /// Runs one probe against its catalog row. Every failure path degrades to Inconclusive with
    /// the failure as evidence, so the remaining probes always run.
    /// </summary>
    public static async Task<ProbeOutcome> RunOneAsync(ICatalogProbe probe, DdlBehaviorCatalog catalog, ProbeSession session)
    {
        var probeName = probe.GetType().Name;
        if (!probe.AppliesTo(session.Edition))
        {
            return new ProbeOutcome(probe.OperationKey, probeName, null, $"not applicable on {session.Edition}");
        }

        var row = catalog.Lookup(probe.OperationKey, session.Version, session.Edition);
        if (row is null)
        {
            return new ProbeOutcome(probe.OperationKey, probeName, VerdictKind.Inconclusive,
                $"no catalog row applies to {session.Edition}/{session.Version}");
        }

        try
        {
            await probe.ArrangeAsync(session);
            var observation = await probe.ActAsync(session);
            var verdict = VerdictEvaluator.Evaluate(row, probe.Expectation, observation);
            return new ProbeOutcome(probe.OperationKey, probeName, verdict.Kind, verdict.Evidence);
        }
        catch (Exception exception)
        {
            return new ProbeOutcome(probe.OperationKey, probeName, VerdictKind.Inconclusive,
                $"probe crashed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            try
            {
                await probe.CleanupAsync(session);
            }
            catch (Exception)
            {
                // Cleanup is best effort: the verification database is disposable.
            }
        }
    }

    /// <summary>
    /// Writes <c>catalog-verification.md</c>, appends the same report to
    /// <c>GITHUB_STEP_SUMMARY</c> when present, and fails the test only when at least one
    /// catalog row was contradicted by measurement.
    /// </summary>
    public static async Task WriteReportAndAssertAsync(
        IReadOnlyList<ProbeOutcome> outcomes, ProbeSession session, string serverDescription)
    {
        var report = VerificationReport.Render(outcomes, serverDescription, session.Edition, session.Version);
        var reportPath = Path.GetFullPath(VerificationReport.FileName);
        await File.WriteAllTextAsync(reportPath, report);
        Console.WriteLine($"Catalog verification report: {reportPath}");

        var stepSummaryPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrWhiteSpace(stepSummaryPath))
        {
            await File.AppendAllTextAsync(stepSummaryPath, report + Environment.NewLine);
        }

        var contradicted = outcomes.Where(o => o.Verdict == VerdictKind.Contradicted).ToList();
        if (contradicted.Count > 0)
        {
            Assert.Fail("Catalog contradicted by measurement:" + Environment.NewLine + string.Join(
                Environment.NewLine,
                contradicted.Select(o => $"- {o.OperationKey}: {o.Evidence}")));
        }
    }
}

/// <summary>Markdown rendering of a verification run; pure and unit tested.</summary>
public static class VerificationReport
{
    public const string FileName = "catalog-verification.md";

    public static string Render(
        IReadOnlyList<ProbeOutcome> outcomes, string serverDescription, SqlEdition edition, SqlServerVersion version)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"## Catalog verification — {serverDescription}");
        builder.AppendLine();
        builder.AppendLine($"Catalog target used for row lookups: edition `{edition}`, version `{version}`.");
        builder.AppendLine("Only **Contradicted** fails the job; Inconclusive rows are follow-up material.");
        builder.AppendLine();
        builder.AppendLine("| Operation key | Probe | Verdict | Evidence |");
        builder.AppendLine("|---|---|---|---|");
        foreach (var outcome in outcomes)
        {
            builder.AppendLine(
                $"| `{outcome.OperationKey}` | {outcome.ProbeName} | {Label(outcome.Verdict)} | {Escape(outcome.Evidence)} |");
        }

        builder.AppendLine();
        var verified = outcomes.Count(o => o.Verdict == VerdictKind.Verified);
        var contradicted = outcomes.Count(o => o.Verdict == VerdictKind.Contradicted);
        var inconclusive = outcomes.Count(o => o.Verdict == VerdictKind.Inconclusive);
        var notApplicable = outcomes.Count(o => o.Verdict is null);
        builder.AppendLine(
            $"**Summary:** {verified} verified, {contradicted} contradicted, " +
            $"{inconclusive} inconclusive, {notApplicable} not applicable.");
        return builder.ToString();
    }

    private static string Label(VerdictKind? verdict) => verdict switch
    {
        VerdictKind.Verified => "Verified",
        VerdictKind.Contradicted => "**Contradicted**",
        VerdictKind.Inconclusive => "Inconclusive",
        null => "n/a",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Unknown verdict."),
    };

    private static string Escape(string evidence) => evidence
        .Replace("|", "\\|")
        .Replace("\r", " ")
        .Replace("\n", " ");
}
