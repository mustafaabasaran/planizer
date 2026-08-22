using Planizer.Cli.Output;
using Planizer.Core;

namespace Planizer.Tests;

/// <summary>Snapshot tests: a fixed <see cref="Report"/> must render to exactly this markdown.</summary>
public class MarkdownWriterTests
{
    private const string Assumption = "SQL Server 2019, Standard edition, offline mode";

    [Fact]
    public void Full_report_renders_expected_markdown_snapshot()
    {
        var report = MakeReport(
            MakeFinding(Severity.Warning, line: 5, message: "Sch-M lock held.", suppressed: true,
                suppressReason: "maintenance window OPS-1"),
            MakeFinding(Severity.Critical, line: 3, message: "Entire table is rewritten.",
                fix: "Add the column as nullable first."),
            MakeFinding(Severity.Info, line: 7, message: "Cannot verify row width.", inconclusive: true));

        var expected = """
            # Planizer report

            **Files:** `migration.sql`
            **Assumption:** SQL Server 2019, Standard edition, offline mode

            | Severity | Rule | Location | Message | Fix |
            | --- | --- | --- | --- | --- |
            | Critical | MSSQL-RW-002 | `migration.sql:3:1` | Entire table is rewritten. | Add the column as nullable first. |
            | Warning | MSSQL-RW-002 | `migration.sql:5:1` | Sch-M lock held. _[suppressed: maintenance window OPS-1]_ | — |
            | Info | MSSQL-RW-002 | `migration.sql:7:1` | Cannot verify row width. _[inconclusive]_ | — |

            <details>
            <summary>Rollback script (incomplete — some statements need a manual rollback)</summary>

            ```sql
            DROP INDEX [IX_T] ON [dbo].[T];
            ```

            </details>

            **Summary:** 2 statements (1 DDL, 1 Sch-M, 0 irreversible, 0 unanalyzable) · findings: 0 Blocker, 1 Critical, 1 Warning, 1 Info (1 suppressed) · rollback incomplete

            """;

        Assert.Equal(expected.ReplaceLineEndings("\n"), WriteMarkdown(report));
    }

    [Fact]
    public void Report_without_findings_or_rollback_says_no_findings_and_omits_details()
    {
        var report = MakeReport() with
        {
            Summary = new ScriptSummary { StatementCount = 1, RollbackComplete = true },
        };

        var markdown = WriteMarkdown(report);

        Assert.Contains("No findings.", markdown);
        Assert.DoesNotContain("<details>", markdown);
        Assert.Contains("rollback complete", markdown);
    }

    [Fact]
    public void Pipes_and_newlines_in_messages_are_escaped_for_the_table()
    {
        var report = MakeReport(
            MakeFinding(Severity.Warning, message: "a | b", fix: "line1\nline2"));

        var markdown = WriteMarkdown(report);

        Assert.Contains(@"a \| b", markdown);
        Assert.Contains("line1<br>line2", markdown);
    }

    // ---- helpers ----

    private static string WriteMarkdown(Report report)
    {
        var writer = new StringWriter();
        new MarkdownReportWriter().Write(report, writer);
        return writer.ToString().ReplaceLineEndings("\n");
    }

    private static Finding MakeFinding(
        Severity severity,
        bool suppressed = false,
        bool inconclusive = false,
        int line = 3,
        string message = "Finding message.",
        string? fix = null,
        string? suppressReason = null)
        => new()
        {
            RuleId = "MSSQL-RW-002",
            Severity = severity,
            Message = message,
            Fix = fix,
            Location = new SourceLocation("migration.sql", line, 1),
            Assumption = Assumption,
            Inconclusive = inconclusive,
            Suppressed = suppressed,
            SuppressReason = suppressReason,
        };

    private static Report MakeReport(params Finding[] findings)
        => new()
        {
            ToolVersion = "0.1.0",
            Dialect = SqlDialect.MsSql,
            TargetVersion = "2019",
            Edition = "Standard",
            Mode = AnalysisMode.Offline,
            Files = ["migration.sql"],
            Findings = findings,
            Summary = new ScriptSummary
            {
                StatementCount = 2,
                DdlCount = 1,
                SchMLockCount = 1,
                RollbackScript = ["DROP INDEX [IX_T] ON [dbo].[T];"],
                RollbackComplete = false,
            },
            SuppressedCount = findings.Count(f => f.Suppressed),
        };
}
