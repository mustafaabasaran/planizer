namespace Planizer.Core;

/// <summary>Source of schema/statistics data the analysis ran with.</summary>
public enum AnalysisMode
{
    /// <summary>SQL text only; no schema or statistics data.</summary>
    Offline,

    /// <summary>SQL text plus a previously captured schema/statistics snapshot.</summary>
    Snapshot,

    /// <summary>SQL text plus a live database connection.</summary>
    Live,
}
