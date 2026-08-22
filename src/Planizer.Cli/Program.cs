using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Text;
using System.Text.Json;
using Planizer.Cli.Output;
using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        var pathsArgument = new Argument<string[]>(
            name: "paths",
            description: "SQL files or directories (searched recursively for *.sql) to analyze.")
        {
            Arity = ArgumentArity.OneOrMore,
        };

        var dialectOption = new Option<string?>(
            name: "--dialect",
            description: "SQL dialect of the scripts. Default: mssql.");
        dialectOption.FromAmong("mssql");

        var targetVersionOption = new Option<string?>(
            name: "--target-version",
            description: "SQL Server version the script will run on (2014, 2016, 2017, 2019, 2022, azure). Default: 2019.");
        targetVersionOption.AddValidator(result =>
        {
            var token = result.GetValueOrDefault<string?>();
            if (token is not null && !TargetParser.TryParseVersion(token, out _))
            {
                result.ErrorMessage =
                    $"Invalid target version '{token}'. Accepted values: 2014, 2016, 2017, 2019, 2022, azure.";
            }
        });

        var editionOption = new Option<string?>(
            name: "--edition",
            description: "SQL Server edition (enterprise, standard, express, azure, developer). Default: standard.");
        editionOption.AddValidator(result =>
        {
            var token = result.GetValueOrDefault<string?>();
            if (token is not null && !TargetParser.TryParseEdition(token, out _))
            {
                result.ErrorMessage =
                    $"Invalid edition '{token}'. Accepted values: enterprise, standard, express, azure, developer.";
            }
        });

        var outputOption = new Option<string>(
            name: "--output",
            getDefaultValue: () => "text",
            description: "Report format written to stdout.");
        outputOption.FromAmong("text", "json", "markdown", "sarif");

        var sarifFileOption = new Option<string?>(
            name: "--sarif-file",
            description: "Also write a SARIF 2.1.0 report to this path (in addition to --output), e.g. for GitHub code scanning.");

        var failOnOption = new Option<string?>(
            name: "--fail-on",
            description: "Lowest severity that makes the exit code 1 (info, warning, critical, blocker). Default: critical.");
        failOnOption.FromAmong("info", "warning", "critical", "blocker");

        var rollbackOption = new Option<bool>(
            name: "--rollback",
            description: "Rollback analysis: generate the reverse script, report statements without an automatic inverse (MSSQL-REV-002) and show the rollback status. Off by default.");

        var noProgressOption = new Option<bool>(
            name: "--no-progress",
            description: "Do not draw the progress indicator on stderr (it is auto-disabled when stderr is not a terminal).");

        var timingOption = new Option<bool>(
            name: "--timing",
            description: "Append a timing block (parse / rules / total, slowest rules) to the text and markdown report.");

        var configOption = new Option<string?>(
            name: "--config",
            description: $"Path to a config file. Default: the nearest {ConfigLoader.DefaultFileName} " +
                "found next to (or above) the first input path, then the current directory.");

        var analyzeCommand = new Command(
            "analyze",
            "Analyze SQL migration scripts for locking, rewrite and reversibility hazards before they run.")
        {
            pathsArgument,
            dialectOption,
            targetVersionOption,
            editionOption,
            outputOption,
            sarifFileOption,
            failOnOption,
            configOption,
            rollbackOption,
            noProgressOption,
            timingOption,
        };

        analyzeCommand.SetHandler(context =>
        {
            var parse = context.ParseResult;
            context.ExitCode = RunAnalyze(
                paths: parse.GetValueForArgument(pathsArgument),
                dialect: parse.GetValueForOption(dialectOption),
                targetVersion: parse.GetValueForOption(targetVersionOption),
                edition: parse.GetValueForOption(editionOption),
                output: parse.GetValueForOption(outputOption) ?? "text",
                sarifFile: parse.GetValueForOption(sarifFileOption),
                failOn: parse.GetValueForOption(failOnOption),
                configPath: parse.GetValueForOption(configOption),
                rollback: parse.GetValueForOption(rollbackOption),
                noProgress: parse.GetValueForOption(noProgressOption),
                showTiming: parse.GetValueForOption(timingOption));
        });

        var rulesCommand = new Command(
            "rules",
            "List every rule with its id, default severity and title.");
        rulesCommand.SetHandler(context =>
        {
            RulesCommand.Write(Console.Out);
            context.ExitCode = 0;
        });

        var rootCommand = new RootCommand(
            "Planizer validates and explains SQL changes (migrations, DDL, DML) before they run.")
        {
            analyzeCommand,
            rulesCommand,
        };

        return new CommandLineBuilder(rootCommand)
            .UseHelp()
            .UseVersionOption()
            .UseParseErrorReporting(errorExitCode: ExitCodeCalculator.ToolError)
            .UseExceptionHandler(errorExitCode: ExitCodeCalculator.ToolError)
            .CancelOnProcessTermination()
            .Build()
            .Invoke(args);
    }

    private static int RunAnalyze(
        string[] paths,
        string? dialect,
        string? targetVersion,
        string? edition,
        string output,
        string? sarifFile,
        string? failOn,
        string? configPath,
        bool rollback = false,
        bool noProgress = false,
        bool showTiming = false)
    {
        try
        {
            var config = ConfigLoader.ApplyOverrides(
                LoadConfig(configPath, paths),
                dialect: dialect is null ? null : SqlDialect.MsSql,
                targetVersion: targetVersion is null ? null : TargetParser.ParseVersion(targetVersion),
                edition: edition is null ? null : TargetParser.ParseEdition(edition),
                failOn: failOn is null ? null : Enum.Parse<Severity>(failOn, ignoreCase: true),
                rollback: rollback ? true : null); // the flag only turns it on; config may enable it too

            if (config.Dialect != SqlDialect.MsSql)
            {
                Console.Error.WriteLine("planizer: error: only the mssql dialect is supported in this version.");
                return ExitCodeCalculator.ToolError;
            }

            var files = SqlFileLocator.Resolve(paths);
            var inputs = files.Select(f => (Path: f, Sql: File.ReadAllText(f))).ToList();

            Report report;
            using (var progress = ProgressRenderer.ShouldShow(noProgress, Console.IsErrorRedirected)
                       ? new ProgressRenderer(Console.Error, TimeSpan.FromMilliseconds(80), ConsoleWidth())
                       : null)
            {
                // The renderer is disposed (line erased) before anything is written to stdout.
                report = new MsSqlAnalyzer().Analyze(inputs, config, progress);
            }

            IReportWriter writer = output switch
            {
                "json" => new JsonReportWriter(),
                "markdown" => new MarkdownReportWriter(showTiming),
                "sarif" => SarifReportWriter.ForMsSql(Environment.CurrentDirectory),
                _ => new TextReportWriter(
                    TextReportWriter.ShouldUseColor(Environment.GetEnvironmentVariable("NO_COLOR"), Console.IsOutputRedirected),
                    showTiming),
            };
            writer.Write(report, Console.Out);

            if (sarifFile is not null)
            {
                WriteSarifFile(report, sarifFile);
            }

            return ExitCodeCalculator.Calculate(report, config.FailOn);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // Tool errors (missing/unreadable file, bad config) — not analysis findings.
            Console.Error.WriteLine($"planizer: error: {ex.Message}");
            return ExitCodeCalculator.ToolError;
        }
    }

    /// <summary>Terminal width for the progress line; 80 when there is no real console.</summary>
    private static int ConsoleWidth()
    {
        try
        {
            return Console.WindowWidth > 0 ? Console.WindowWidth : 80;
        }
        catch (IOException)
        {
            return 80;
        }
    }

    /// <summary>Writes the SARIF report next to whatever --output produced; parent directories are created.</summary>
    private static void WriteSarifFile(Report report, string sarifFile)
    {
        var fullPath = Path.GetFullPath(sarifFile);
        if (Path.GetDirectoryName(fullPath) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        using var file = new StreamWriter(fullPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        SarifReportWriter.ForMsSql(Environment.CurrentDirectory).Write(report, file);
    }

    private static PlanizerConfig LoadConfig(string? configPath, string[] paths)
    {
        if (configPath is not null)
        {
            return ConfigLoader.LoadFile(configPath);
        }

        return ConfigLocator.FindConfigFile(paths, Environment.CurrentDirectory) is { } discovered
            ? ConfigLoader.LoadFile(discovered)
            : new PlanizerConfig();
    }
}
