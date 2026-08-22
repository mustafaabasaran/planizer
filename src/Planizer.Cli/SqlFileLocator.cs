namespace Planizer.Cli;

/// <summary>Resolves CLI path arguments (files or directories) to the list of .sql files to analyze.</summary>
public static class SqlFileLocator
{
    /// <summary>
    /// Explicit files are kept in argument order; each directory is searched recursively for
    /// <c>*.sql</c> and its matches are sorted by path (ordinal) so reports are deterministic.
    /// Throws <see cref="FileNotFoundException"/> for a missing path and when no .sql file is
    /// found at all.
    /// </summary>
    public static IReadOnlyList<string> Resolve(IEnumerable<string> paths)
    {
        var files = new List<string>();

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                files.AddRange(
                    Directory.EnumerateFiles(path, "*.sql", SearchOption.AllDirectories)
                        .OrderBy(f => f, StringComparer.Ordinal));
            }
            else if (File.Exists(path))
            {
                files.Add(path);
            }
            else
            {
                throw new FileNotFoundException($"File or directory not found: {path}", path);
            }
        }

        if (files.Count == 0)
        {
            throw new FileNotFoundException("No .sql files found in the given paths.");
        }

        return files.Distinct(StringComparer.Ordinal).ToList();
    }
}
