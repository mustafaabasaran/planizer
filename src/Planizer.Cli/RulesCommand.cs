using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Cli;

/// <summary>
/// The <c>planizer rules</c> subcommand: every rule id with its default severity and title.
/// Includes MSSQL-PARSE-001, which the analyzer produces itself rather than via a rule class.
/// </summary>
public static class RulesCommand
{
    /// <summary>Title shown for MSSQL-PARSE-001, which has no rule class of its own.</summary>
    public const string ParseRuleTitle = "SQL script does not parse (produced by the analyzer itself)";

    public static void Write(TextWriter output)
    {
        var rows = MsSqlAnalyzer.DiscoverRules()
            .Select(rule => (rule.Id, Severity: rule.DefaultSeverity.ToString(), rule.Title))
            .Append((
                Id: MsSqlAnalyzer.ParseRuleId,
                Severity: nameof(Severity.Blocker),
                Title: ParseRuleTitle))
            .OrderBy(row => row.Id, StringComparer.Ordinal);

        output.WriteLine($"{"RULE ID",-17}{"SEVERITY",-9}TITLE");
        output.WriteLine(new string('-', 60));
        foreach (var (id, severity, title) in rows)
        {
            output.WriteLine($"{id,-17}{severity,-9}{title}");
        }
    }
}
