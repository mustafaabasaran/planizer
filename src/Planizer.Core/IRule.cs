namespace Planizer.Core;

/// <summary>A deterministic analysis rule. Implementations are discovered by the dialect analyzer.</summary>
public interface IRule
{
    /// <summary>e.g. "MSSQL-LOCK-001".</summary>
    string Id { get; }

    /// <summary>Short English title.</summary>
    string Title { get; }

    Severity DefaultSeverity { get; }

    IEnumerable<Finding> Analyze(IAnalysisContext context);
}
