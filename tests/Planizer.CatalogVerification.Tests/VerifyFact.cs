namespace Planizer.CatalogVerification.Tests;

/// <summary>
/// The single gate of the whole verification harness: <c>PLANIZER_CATALOG_VERIFY=1</c>.
/// Development machines never set it, so nothing in this project ever starts a container or
/// opens a SQL Server connection outside the GitHub Actions verification job.
/// </summary>
public static class VerificationGate
{
    public const string GateVariable = "PLANIZER_CATALOG_VERIFY";

    public static bool IsEnabled => Environment.GetEnvironmentVariable(GateVariable) == "1";
}

/// <summary>
/// A fact that runs only when <see cref="VerificationGate"/> is enabled (the CI verification
/// job). Everywhere else the test is reported as Skipped at discovery time, before any fixture
/// or measurement code can run.
/// </summary>
public sealed class VerifyFactAttribute : FactAttribute
{
    public VerifyFactAttribute()
    {
        if (!VerificationGate.IsEnabled)
        {
            Skip = $"Catalog verification runs only in CI (set {VerificationGate.GateVariable}=1).";
        }
    }
}
