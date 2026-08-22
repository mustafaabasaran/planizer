using Planizer.Cli;
using Planizer.MsSql;

namespace Planizer.Tests;

public class RulesCommandTests
{
    [Fact]
    public void Lists_every_rule_id_sorted_with_severity_and_title()
    {
        var writer = new StringWriter();
        RulesCommand.Write(writer);
        var lines = writer.ToString()
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Header + separator + every reflection-discovered rule + MSSQL-PARSE-001.
        var expectedRuleCount = MsSqlAnalyzer.DiscoverRules().Count + 1;
        Assert.Equal(expectedRuleCount + 2, lines.Length);
        Assert.StartsWith("RULE ID", lines[0]);

        var ids = lines.Skip(2).Select(l => l.Split(' ', 2)[0]).ToList();
        Assert.Equal(expectedRuleCount, ids.Count);
        Assert.Equal(ids.OrderBy(i => i, StringComparer.Ordinal), ids);
        Assert.Contains("MSSQL-PARSE-001", ids);
        Assert.Contains("MSSQL-LOCK-001", ids);
        Assert.Contains("MSSQL-RW-016", ids);
        Assert.Contains("MSSQL-REV-005", ids);
        Assert.Contains("MSSQL-DYN-001", ids);
        Assert.Contains("MSSQL-IDEM-001", ids);
        Assert.Contains("MSSQL-IDEM-003", ids);
        Assert.Contains("MSSQL-BATCH-001", ids);
        Assert.Contains("MSSQL-LIT-001", ids);
        Assert.Contains("MSSQL-TRAN-001", ids);
        Assert.Contains("MSSQL-TRAN-006", ids);
        Assert.Contains("MSSQL-SET-001", ids);
        Assert.Contains("MSSQL-ENV-003", ids);
        Assert.Contains("MSSQL-LIM-001", ids);
        Assert.Contains("MSSQL-VER-001", ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        var parseLine = lines.Single(l => l.StartsWith("MSSQL-PARSE-001", StringComparison.Ordinal));
        Assert.Contains("Blocker", parseLine);
    }

    [Fact]
    public void Every_row_has_a_severity_and_a_nonempty_title()
    {
        var writer = new StringWriter();
        RulesCommand.Write(writer);
        var rows = writer.ToString()
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(2);

        foreach (var row in rows)
        {
            var severity = row[17..26].Trim();
            Assert.Contains(severity, new[] { "Info", "Warning", "Critical", "Blocker" });
            Assert.False(string.IsNullOrWhiteSpace(row[26..]), $"missing title in row: {row}");
        }
    }
}
