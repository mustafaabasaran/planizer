namespace Planizer.CatalogVerification.Tests;

/// <summary>
/// The single CI entry point: runs every discovered probe against the live server, writes the
/// markdown report (file + GITHUB_STEP_SUMMARY), and fails only when a catalog row is
/// Contradicted by measurement. Locally this test is always Skipped by <see cref="VerifyFactAttribute"/>.
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class CatalogVerificationTests
{
    private readonly ServerFixture _server;

    public CatalogVerificationTests(ServerFixture server) => _server = server;

    [VerifyFact]
    public async Task Every_probe_confirms_its_catalog_row()
    {
        var session = ProbeSession.From(_server);
        var outcomes = await ProbeRunner.RunAllAsync(session);
        await ProbeRunner.WriteReportAndAssertAsync(outcomes, session, _server.EditionDescription);
    }
}
