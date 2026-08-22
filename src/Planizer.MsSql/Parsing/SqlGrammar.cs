using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;

namespace Planizer.MsSql.Parsing;

/// <summary>
/// A ScriptDom grammar level (<c>TSql120Parser</c> … <c>TSql180Parser</c>) with its user-facing
/// label. Every target version maps to one grammar; the levels beyond the newest target (2025
/// and the post-2025 preview grammar) exist so the analyzer can tell "syntax from a newer SQL
/// Server" (MSSQL-VER-001) apart from "not T-SQL at all" (MSSQL-PARSE-001).
/// </summary>
public sealed record SqlGrammar(int Level, string Label)
{
    public static readonly SqlGrammar Sql2014 = new(120, "2014");
    public static readonly SqlGrammar Sql2016 = new(130, "2016");
    public static readonly SqlGrammar Sql2017 = new(140, "2017");
    public static readonly SqlGrammar Sql2019 = new(150, "2019");
    public static readonly SqlGrammar Sql2022 = new(160, "2022");
    public static readonly SqlGrammar Sql2025 = new(170, "2025");
    public static readonly SqlGrammar Preview = new(180, "post-2025 preview");

    /// <summary>Every grammar ScriptDom ships for a supported target or newer, oldest first.</summary>
    public static IReadOnlyList<SqlGrammar> All { get; } =
        [Sql2014, Sql2016, Sql2017, Sql2019, Sql2022, Sql2025, Preview];

    /// <summary>Whether <c>--target-version</c> accepts this grammar's label (2025+ are not targets yet).</summary>
    public bool IsTargetVersion => Level <= Sql2022.Level;

    /// <summary>The grammar a target version is parsed with. Azure SQL uses the 2022 grammar.</summary>
    public static SqlGrammar For(SqlServerVersion version) => version switch
    {
        SqlServerVersion.Sql2014 => Sql2014,
        SqlServerVersion.Sql2016 => Sql2016,
        SqlServerVersion.Sql2017 => Sql2017,
        SqlServerVersion.Sql2019 => Sql2019,
        SqlServerVersion.Sql2022 or SqlServerVersion.AzureSql => Sql2022,
        _ => Sql2022,
    };

    /// <summary>Grammars newer than the one a target is parsed with, oldest first.</summary>
    public static IReadOnlyList<SqlGrammar> NewerThan(SqlServerVersion version)
    {
        var level = For(version).Level;
        return All.Where(g => g.Level > level).ToList();
    }

    internal TSqlParser CreateParser() => Level switch
    {
        120 => new TSql120Parser(initialQuotedIdentifiers: true),
        130 => new TSql130Parser(initialQuotedIdentifiers: true),
        140 => new TSql140Parser(initialQuotedIdentifiers: true),
        150 => new TSql150Parser(initialQuotedIdentifiers: true),
        160 => new TSql160Parser(initialQuotedIdentifiers: true),
        170 => new TSql170Parser(initialQuotedIdentifiers: true),
        180 => new TSql180Parser(initialQuotedIdentifiers: true),
        _ => throw new ArgumentOutOfRangeException(nameof(Level), Level, "Unknown ScriptDom grammar level."),
    };
}
