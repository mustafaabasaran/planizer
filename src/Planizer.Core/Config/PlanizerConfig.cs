namespace Planizer.Core;

/// <summary>
/// Effective analysis configuration. Defaults are the worst-case assumption:
/// SQL Server 2019, Standard edition, fail on Critical.
/// </summary>
public sealed record PlanizerConfig
{
    // set, not init: the source-generated deserializer writes init-only properties through an
    // object initializer even when the JSON omits them, clobbering these defaults with default(T).
    // Settable properties are only assigned when present in the JSON.
    public SqlDialect Dialect { get; set; } = SqlDialect.MsSql;
    public SqlServerVersion TargetVersion { get; set; } = SqlServerVersion.Sql2019;
    public SqlEdition Edition { get; set; } = SqlEdition.Standard;
    public Severity FailOn { get; set; } = Severity.Critical;

    /// <summary>
    /// Opt-in rollback analysis (<c>--rollback</c>): generate the reverse script, report statements
    /// without an automatic inverse (MSSQL-REV-002) and print the rollback status. Off by default —
    /// most teams fix forward, and the data-loss rule (MSSQL-REV-001) stays on regardless.
    /// </summary>
    public bool Rollback { get; set; }

    /// <summary>Keyed by rule id, e.g. "MSSQL-LOCK-001". Unknown ids are not an error.</summary>
    public IReadOnlyDictionary<string, RuleOverride> Rules { get; set; }
        = new Dictionary<string, RuleOverride>();
}
