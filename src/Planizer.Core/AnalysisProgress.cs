namespace Planizer.Core;

/// <summary>Where an analysis run currently is; reported through <see cref="IProgress{T}"/>.</summary>
public enum AnalysisPhase
{
    /// <summary>Reading and parsing input files; <c>Label</c> is the file path.</summary>
    Parsing,

    /// <summary>Running rules over the parsed statements; <c>Label</c> is the rule id.</summary>
    Rules,

    /// <summary>Resolving suppressions and building the summary.</summary>
    Finishing,
}

/// <summary>
/// One progress tick. <see cref="Current"/> is 1-based and reported <em>before</em> the unit of
/// work starts, so a renderer can show what is being processed right now.
/// </summary>
public sealed record AnalysisProgress(AnalysisPhase Phase, int Current, int Total, string Label);
