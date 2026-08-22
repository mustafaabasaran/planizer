using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Planizer.MsSql.Rules.Locking;

/// <summary>
/// Shared AST helpers for the locking rule family: reading index options
/// (ONLINE, RESUMABLE, WAIT_AT_LOW_PRIORITY) and naming an index statement's target table.
/// AST property names verified against Microsoft.SqlServer.TransactSql.ScriptDom 180.78.1.
/// </summary>
internal static class IndexOptionInspector
{
    /// <summary>Whether the option list carries <c>ONLINE = ON</c>.</summary>
    public static bool IsOnline(IEnumerable<IndexOption> options)
        => options.OfType<IndexStateOption>()
            .Any(o => o.OptionKind == IndexOptionKind.Online && o.OptionState == OptionState.On);

    /// <summary>Whether the option list carries <c>RESUMABLE = ON</c>.</summary>
    public static bool IsResumable(IEnumerable<IndexOption> options)
        => options.OfType<IndexStateOption>()
            .Any(o => o.OptionKind == IndexOptionKind.Resumable && o.OptionState == OptionState.On);

    /// <summary>
    /// Whether <c>WAIT_AT_LOW_PRIORITY</c> is specified, either nested inside
    /// <c>ONLINE = ON (WAIT_AT_LOW_PRIORITY (…))</c> (an <see cref="OnlineIndexOption"/> with a
    /// non-null <see cref="OnlineIndexOption.LowPriorityLockWaitOption"/>) or as a standalone option.
    /// </summary>
    public static bool HasWaitAtLowPriority(IEnumerable<IndexOption> options)
        => options.Any(o => o is OnlineIndexOption { LowPriorityLockWaitOption: not null }
            or WaitAtLowPriorityOption);

    /// <summary>Dotted display name of the table an index statement operates on.</summary>
    public static string TargetTable(IndexStatement statement)
        => Render(statement.OnName);

    /// <summary>Dotted display name of a schema object; a placeholder when absent.</summary>
    public static string Render(SchemaObjectName? name)
        => name is null || name.Identifiers.Count == 0
            ? "the table"
            : string.Join(".", name.Identifiers.Select(i => i.Value));
}
