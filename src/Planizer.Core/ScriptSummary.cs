namespace Planizer.Core;

/// <summary>Script-level aggregate numbers shown at the end of a report.</summary>
public sealed record ScriptSummary
{
    public int StatementCount { get; init; }
    public int DdlCount { get; init; }

    /// <summary>Number of statements that take a Sch-M lock.</summary>
    public int SchMLockCount { get; init; }

    public int IrreversibleCount { get; init; }

    /// <summary>Statements that cannot be analyzed statically (dynamic SQL etc.).</summary>
    public int UnanalyzableCount { get; init; }

    public IReadOnlyList<string> RollbackScript { get; init; } = [];

    /// <summary>Whether a reverse statement could be generated for every statement.</summary>
    /// <summary>
    /// Whether every state-changing statement got an automatic inverse; <c>null</c> when rollback
    /// analysis was not requested (<see cref="Config.PlanizerConfig.Rollback"/>).
    /// </summary>
    public bool? RollbackComplete { get; init; }
}
