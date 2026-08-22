using Planizer.Cli;

namespace Planizer.Tests;

/// <summary>
/// <see cref="ConfigLocator"/>: without an explicit <c>--config</c> the nearest
/// <c>.planizer.json</c> to the analyzed files wins, then the working directory.
/// </summary>
public sealed class ConfigLocatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("planizer-config-tests").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Config_next_to_the_analyzed_file_wins_over_the_working_directory()
    {
        var samples = Directory.CreateDirectory(Path.Combine(_root, "samples")).FullName;
        var nearConfig = WriteConfig(samples);
        WriteConfig(_root); // the working directory has one too — the nearer one wins
        var migration = WriteFile(samples, "001.sql");

        Assert.Equal(nearConfig, ConfigLocator.FindConfigFile([migration], _root));
    }

    [Fact]
    public void Ancestor_directories_of_the_input_are_probed_upward()
    {
        var deep = Directory.CreateDirectory(Path.Combine(_root, "a", "b")).FullName;
        var rootConfig = WriteConfig(_root);
        var migration = WriteFile(deep, "001.sql");

        Assert.Equal(rootConfig, ConfigLocator.FindConfigFile([migration], _root));
    }

    [Fact]
    public void A_directory_input_is_probed_itself_first()
    {
        var samples = Directory.CreateDirectory(Path.Combine(_root, "samples")).FullName;
        var config = WriteConfig(samples);

        Assert.Equal(config, ConfigLocator.FindConfigFile([samples], _root));
    }

    [Fact]
    public void Relative_input_paths_resolve_against_the_working_directory()
    {
        var samples = Directory.CreateDirectory(Path.Combine(_root, "samples")).FullName;
        var config = WriteConfig(samples);
        WriteFile(samples, "001.sql");

        Assert.Equal(config, ConfigLocator.FindConfigFile([Path.Combine("samples", "001.sql")], _root));
    }

    [Fact]
    public void Working_directory_is_the_fallback_for_inputs_outside_its_tree()
    {
        var elsewhere = Directory.CreateTempSubdirectory("planizer-elsewhere");
        try
        {
            var cwdConfig = WriteConfig(_root);
            var migration = WriteFile(elsewhere.FullName, "001.sql");

            Assert.Equal(cwdConfig, ConfigLocator.FindConfigFile([migration], _root));
        }
        finally
        {
            elsewhere.Delete(recursive: true);
        }
    }

    [Fact]
    public void No_config_anywhere_returns_null()
    {
        var migration = WriteFile(_root, "001.sql");

        Assert.Null(ConfigLocator.FindConfigFile([migration], _root));
    }

    private static string WriteConfig(string directory)
    {
        var path = Path.Combine(directory, ".planizer.json");
        File.WriteAllText(path, "{}");
        return path;
    }

    private static string WriteFile(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "SELECT 1;");
        return path;
    }
}
