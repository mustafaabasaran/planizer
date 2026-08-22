namespace Planizer.Core;

/// <summary>The complete analysis result for one run.</summary>
public sealed record Report
{
    public required string ToolVersion { get; init; }
    public required SqlDialect Dialect { get; init; }

    /// <summary>e.g. "2019".</summary>
    public required string TargetVersion { get; init; }

    /// <summary>e.g. "Standard".</summary>
    public required string Edition { get; init; }

    public required AnalysisMode Mode { get; init; }
    public required IReadOnlyList<string> Files { get; init; }
    public required IReadOnlyList<Finding> Findings { get; init; }
    public required ScriptSummary Summary { get; init; }
    public int SuppressedCount { get; init; }

    /// <summary>Where the time went (parse / per rule / total); null for reports not produced by an analyzer run.</summary>
    public AnalysisTiming? Timing { get; init; }
}
