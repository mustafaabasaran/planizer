using Planizer.Core;

namespace Planizer.Cli;

/// <summary>
/// Finds the <c>.planizer.json</c> an analyze run should use when no explicit <c>--config</c> is
/// given. The nearest config <em>to the analyzed files</em> wins: starting at the first input
/// path (its own directory for a file), every ancestor directory is probed up to the filesystem
/// root. Only then does the current working directory serve as a fallback — so
/// <c>planizer analyze samples/migration.sql</c> run from the repo root still honors
/// <c>samples/.planizer.json</c>.
/// </summary>
public static class ConfigLocator
{
    /// <summary>
    /// Full path of the config file to load, or <c>null</c> when none exists. Nonexistent input
    /// paths are not an error here — file resolution reports those separately.
    /// </summary>
    public static string? FindConfigFile(IReadOnlyList<string> inputPaths, string workingDirectory)
    {
        if (inputPaths.Count > 0 && StartDirectory(inputPaths[0], workingDirectory) is { } start)
        {
            for (var directory = start; directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, ConfigLoader.DefaultFileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        var fallback = Path.Combine(workingDirectory, ConfigLoader.DefaultFileName);
        return File.Exists(fallback) ? fallback : null;
    }

    private static DirectoryInfo? StartDirectory(string inputPath, string workingDirectory)
    {
        var fullPath = Path.GetFullPath(inputPath, workingDirectory);
        var directory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        return directory is null ? null : new DirectoryInfo(directory);
    }
}
