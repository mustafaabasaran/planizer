namespace Planizer.Core;

/// <summary>Null object for offline mode: no statistics data. Rules must go Inconclusive, not silent.</summary>
public sealed class UnavailableStatsProvider : IStatsProvider
{
    public static UnavailableStatsProvider Instance { get; } = new();

    private UnavailableStatsProvider()
    {
    }

    public bool IsAvailable => false;
}
