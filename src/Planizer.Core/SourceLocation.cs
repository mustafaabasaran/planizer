namespace Planizer.Core;

/// <summary>Position of a statement or finding inside an analyzed file (1-based line/column).</summary>
public sealed record SourceLocation(string File, int Line, int Column);
