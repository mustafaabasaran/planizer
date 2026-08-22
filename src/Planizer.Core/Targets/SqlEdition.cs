namespace Planizer.Core;

/// <summary>Target SQL Server edition. Developer input is mapped to <see cref="Enterprise"/> (same behavior).</summary>
public enum SqlEdition
{
    Enterprise,
    Standard,
    Express,
    Azure,
}
