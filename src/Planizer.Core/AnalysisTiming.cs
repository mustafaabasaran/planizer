namespace Planizer.Core;

/// <summary>Wall-clock cost of one rule over the whole input.</summary>
public sealed record RuleTiming(string RuleId, double ElapsedMs, int FindingCount);

/// <summary>Where the time went; always collected, shown on request (<c>--timing</c>) or via JSON.</summary>
public sealed record AnalysisTiming
{
    public double ParseMs { get; init; }
    public double RulesMs { get; init; }
    public double TotalMs { get; init; }
    public IReadOnlyList<RuleTiming> Rules { get; init; } = [];

    /// <summary>Rules ordered by cost, most expensive first.</summary>
    public IEnumerable<RuleTiming> Slowest(int count)
        => Rules.OrderByDescending(r => r.ElapsedMs).ThenBy(r => r.RuleId, StringComparer.Ordinal).Take(count);
}
