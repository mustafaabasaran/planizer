namespace Planizer.MsSql;

/// <summary>Lock taken by a DDL operation; mirrors the CSV <c>lock</c> column one-to-one.</summary>
public enum LockLevel
{
    /// <summary>Full schema-modification lock, held for the duration (CSV: <c>schm</c>).</summary>
    SchM,

    /// <summary>Brief schema-modification lock, e.g. metadata-only changes (CSV: <c>schm_brief</c>).</summary>
    SchMBrief,

    /// <summary>Shared table lock held for the duration — reads allowed, writes blocked (CSV: <c>s_table</c>).</summary>
    STable,

    /// <summary>
    /// Brief shared (S) table lock, taken only for the short preparation and final phases of an
    /// online index operation rather than for the whole build (CSV: <c>s_brief</c>). Distinct from
    /// <see cref="STable"/> (held throughout) and from <see cref="SchMBrief"/>: an S lock blocks
    /// writers but not readers, and it never blocks all access the way a Sch-M lock does.
    /// </summary>
    SBrief,

    /// <summary>No schema-level lock of note (CSV: <c>none</c>).</summary>
    None,
}

/// <summary>Data movement caused by a DDL operation; mirrors the CSV <c>data_movement</c> column.</summary>
public enum DataMovement
{
    /// <summary>Only system metadata changes; no row is touched (CSV: <c>metadata_only</c>).</summary>
    MetadataOnly,

    /// <summary>Every row of the table is rewritten (CSV: <c>rewrite</c>).</summary>
    Rewrite,

    /// <summary>Every row is read for validation, none rewritten (CSV: <c>full_scan</c>).</summary>
    FullScan,

    /// <summary>A new index structure is built from the data (CSV: <c>index_build</c>).</summary>
    IndexBuild,

    /// <summary>The statement errors out when the table has rows (CSV: <c>fails_if_rows</c>).</summary>
    FailsIfRows,

    /// <summary>Pages are deallocated wholesale, e.g. TRUNCATE (CSV: <c>deallocate</c>).</summary>
    Deallocate,

    /// <summary>No data movement at all, e.g. DROP TABLE (CSV: <c>none</c>).</summary>
    None,
}

/// <summary>Whether a DDL operation can be reversed; mirrors the CSV <c>reversible</c> column.</summary>
public enum Reversibility
{
    /// <summary>A straightforward inverse statement exists (CSV: <c>yes</c>).</summary>
    Yes,

    /// <summary>Cannot be undone without a restore (CSV: <c>no</c>).</summary>
    No,

    /// <summary>Reversible, but only with a dedicated script (CSV: <c>with_script</c>).</summary>
    WithScript,
}

/// <summary>
/// Behavior of one DDL operation under a specific version/edition, as resolved from the
/// embedded behavior table (<c>mssql-ddl-behavior.csv</c>).
/// </summary>
public sealed record DdlBehavior(LockLevel Lock, DataMovement Movement, Reversibility Reversible, string? Notes);
