namespace Planizer.Core;

/// <summary>
/// Parses user-facing target tokens: <c>--target-version 2019</c> → <see cref="SqlServerVersion.Sql2019"/>,
/// <c>azure</c> → <see cref="SqlServerVersion.AzureSql"/>, <c>developer</c> → <see cref="SqlEdition.Enterprise"/>.
/// </summary>
public static class TargetParser
{
    public static SqlServerVersion ParseVersion(string? value)
        => TryParseVersion(value, out var version)
            ? version
            : throw new ArgumentException(
                $"Invalid target version '{value}'. Accepted values: 2014, 2016, 2017, 2019, 2022, azure.",
                nameof(value));

    public static bool TryParseVersion(string? value, out SqlServerVersion version)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "2014":
                version = SqlServerVersion.Sql2014;
                return true;
            case "2016":
                version = SqlServerVersion.Sql2016;
                return true;
            case "2017":
                version = SqlServerVersion.Sql2017;
                return true;
            case "2019":
                version = SqlServerVersion.Sql2019;
                return true;
            case "2022":
                version = SqlServerVersion.Sql2022;
                return true;
            case "azure":
                version = SqlServerVersion.AzureSql;
                return true;
            default:
                version = default;
                return false;
        }
    }

    public static SqlEdition ParseEdition(string? value)
        => TryParseEdition(value, out var edition)
            ? edition
            : throw new ArgumentException(
                $"Invalid edition '{value}'. Accepted values: enterprise, standard, express, azure, developer.",
                nameof(value));

    public static bool TryParseEdition(string? value, out SqlEdition edition)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "enterprise":
            case "developer": // Developer behaves like Enterprise.
                edition = SqlEdition.Enterprise;
                return true;
            case "standard":
                edition = SqlEdition.Standard;
                return true;
            case "express":
                edition = SqlEdition.Express;
                return true;
            case "azure":
                edition = SqlEdition.Azure;
                return true;
            default:
                edition = default;
                return false;
        }
    }

    /// <summary>User-facing token for a version, e.g. "2019" or "azure".</summary>
    public static string VersionToken(SqlServerVersion version) => version switch
    {
        SqlServerVersion.Sql2014 => "2014",
        SqlServerVersion.Sql2016 => "2016",
        SqlServerVersion.Sql2017 => "2017",
        SqlServerVersion.Sql2019 => "2019",
        SqlServerVersion.Sql2022 => "2022",
        SqlServerVersion.AzureSql => "azure",
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown SQL Server version."),
    };

    /// <summary>User-facing token for an edition, e.g. "Standard".</summary>
    public static string EditionToken(SqlEdition edition) => edition switch
    {
        SqlEdition.Enterprise => "Enterprise",
        SqlEdition.Standard => "Standard",
        SqlEdition.Express => "Express",
        SqlEdition.Azure => "Azure",
        _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "Unknown SQL Server edition."),
    };
}
