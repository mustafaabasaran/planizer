using Planizer.Core;

namespace Planizer.Cli;

/// <summary>
/// Maps a report to the process exit code contract:
/// 0 = below threshold, 1 = unsuppressed finding at/above <c>--fail-on</c>, 2 = tool error.
/// </summary>
public static class ExitCodeCalculator
{
    public const int Pass = 0;
    public const int FindingsAtOrAboveThreshold = 1;
    public const int ToolError = 2;

    /// <summary>Suppressed findings stay in the report but never count toward the exit code.</summary>
    public static int Calculate(Report report, Severity failOn)
        => report.Findings.Any(f => !f.Suppressed && f.Severity >= failOn)
            ? FindingsAtOrAboveThreshold
            : Pass;
}
