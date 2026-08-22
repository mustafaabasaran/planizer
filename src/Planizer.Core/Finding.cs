namespace Planizer.Core;

/// <summary>A single rule result. Every field a report consumer (human or CI) needs is here.</summary>
public sealed record Finding
{
    public required string RuleId { get; init; }
    public required Severity Severity { get; init; }

    /// <summary>One-sentence rationale.</summary>
    public required string Message { get; init; }

    /// <summary>Suggested fix; SQL when possible.</summary>
    public string? Fix { get; init; }

    public required SourceLocation Location { get; init; }

    /// <summary>First ~120 characters of the offending statement.</summary>
    public string? StatementSummary { get; init; }

    /// <summary>Production assumption, e.g. "SQL Server 2019, Standard edition, offline mode".</summary>
    public required string Assumption { get; init; }

    /// <summary>When data is missing the rule does not stay silent; it flags this instead.</summary>
    public bool Inconclusive { get; init; }

    public bool Suppressed { get; init; }
    public string? SuppressReason { get; init; }
}
