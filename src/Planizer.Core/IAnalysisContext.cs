namespace Planizer.Core;

/// <summary>Everything a rule may consult during analysis. Dialect adapters extend this.</summary>
public interface IAnalysisContext
{
    AnalysisMode Mode { get; }
    PlanizerConfig Config { get; }
    ISchemaProvider Schema { get; }
    IStatsProvider Stats { get; }

    /// <summary>Copied verbatim into every finding, e.g. "SQL Server 2019, Standard edition, offline mode".</summary>
    string AssumptionText { get; }
}
