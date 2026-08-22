namespace Planizer.Core;

/// <summary>
/// Effective analysis configuration. Defaults are the worst-case assumption:
/// SQL Server 2019, Standard edition, fail on Critical.
/// </summary>
public sealed record PlanizerConfig
{
    public SqlDialect Dialect { get; init; } = SqlDialect.MsSql;
    public SqlServerVersion TargetVersion { get; init; } = SqlServerVersion.Sql2019;
    public SqlEdition Edition { get; init; } = SqlEdition.Standard;
    public Severity FailOn { get; init; } = Severity.Critical;

    /// <summary>
    /// Opt-in rollback analysis (<c>--rollback</c>): generate the reverse script, report statements
    /// without an automatic inverse (MSSQL-REV-002) and print the rollback status. Off by default —
    /// most teams fix forward, and the data-loss rule (MSSQL-REV-001) stays on regardless.
    /// </summary>
    public bool Rollback { get; init; }

    /// <summary>Keyed by rule id, e.g. "MSSQL-LOCK-001". Unknown ids are not an error.</summary>
    public IReadOnlyDictionary<string, RuleOverride> Rules { get; init; }
        = new Dictionary<string, RuleOverride>();
}
