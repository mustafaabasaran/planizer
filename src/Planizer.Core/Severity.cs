namespace Planizer.Core;

/// <summary>Finding severity. Order matters: <c>Info &lt; Warning &lt; Critical &lt; Blocker</c>.</summary>
public enum Severity
{
    Info,
    Warning,
    Critical,
    Blocker,
}
