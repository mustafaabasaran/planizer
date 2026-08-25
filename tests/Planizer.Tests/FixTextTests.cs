using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

/// <summary>
/// Suggested-fix wording that has to stay correct against the documented SQL Server behavior.
/// Fixtures only assert rule/severity/line, so the exact advice is checked here:
/// RESUMABLE inside an explicit transaction (error 574), DBCC CLEANTABLE versus a rebuild after
/// DROP COLUMN, and the collision-proof, data-only backup copy of MSSQL-REV-001.
/// </summary>
public class FixTextTests
{
    private static Report Analyze(string sql, PlanizerConfig? config = null)
        => new MsSqlAnalyzer().Analyze([("m.sql", sql)], config ?? new PlanizerConfig());

    private static PlanizerConfig Enterprise2019 => new()
    {
        TargetVersion = SqlServerVersion.Sql2019,
        Edition = SqlEdition.Enterprise,
    };

    // ---- MSSQL-LOCK-005 -----------------------------------------------------------------

    [Fact]
    public void Resumable_fix_outside_a_transaction_suggests_the_option()
    {
        var report = Analyze(
            "CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);", Enterprise2019);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LOCK-005");
        Assert.StartsWith("Add RESUMABLE = ON, MAX_DURATION = 60", finding.Fix);
    }

    [Fact]
    public void Resumable_fix_always_warns_about_a_runner_owned_transaction()
    {
        var report = Analyze(
            "CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);", Enterprise2019);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LOCK-005");
        Assert.Contains("migration runner wraps the script in its own transaction", finding.Fix);
    }

    [Fact]
    public void Resumable_fix_inside_an_explicit_transaction_says_to_move_the_statement_out()
    {
        const string sql = """
            BEGIN TRANSACTION;
            CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON);
            COMMIT;
            """;

        var report = Analyze(sql, Enterprise2019);

        // The finding itself stands: progress is lost on abort either way.
        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LOCK-005");
        Assert.Equal(Severity.Info, finding.Severity);

        // ... but following "add RESUMABLE = ON" here would turn a working migration into Msg 574.
        Assert.NotNull(finding.Fix);
        Assert.StartsWith("RESUMABLE = ON cannot be added here", finding.Fix);
        Assert.Contains("error 574", finding.Fix);
        Assert.Contains("BEGIN TRANSACTION", finding.Fix);
    }

    [Fact]
    public void Resumable_fix_inside_a_transaction_also_applies_to_a_rebuild()
    {
        const string sql = """
            BEGIN TRAN;
            ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON);
            COMMIT;
            """;

        var report = Analyze(sql, Enterprise2019);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LOCK-005");
        Assert.Contains("Move the ALTER INDEX REBUILD out of the BEGIN TRANSACTION", finding.Fix);
    }

    // ---- MSSQL-RW-010 -------------------------------------------------------------------

    [Fact]
    public void Drop_column_fix_separates_cleantable_from_rebuild_by_column_type()
    {
        var report = Analyze("ALTER TABLE dbo.Orders DROP COLUMN LegacyCode;");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-RW-010");
        Assert.NotNull(finding.Fix);

        // CLEANTABLE is offered only for the variable-length / LOB case ...
        Assert.Contains("Variable-length or LOB", finding.Fix);
        Assert.Contains("DBCC CLEANTABLE (0, 'dbo.Orders');", finding.Fix);

        // ... and the fixed-length case is explicitly told that CLEANTABLE does nothing there.
        Assert.Contains("Fixed-length", finding.Fix);
        Assert.Contains("CLEANTABLE reclaims nothing there", finding.Fix);
        Assert.Contains("ALTER INDEX ALL ON dbo.Orders REBUILD;", finding.Fix);
    }

    [Fact]
    public void Drop_column_fix_on_a_temp_table_does_not_offer_cleantable()
    {
        // DBCC CLEANTABLE is not supported on temporary tables.
        var report = Analyze("ALTER TABLE #Staging DROP COLUMN LegacyCode;");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-RW-010");
        Assert.NotNull(finding.Fix);
        Assert.DoesNotContain("DBCC CLEANTABLE (0,", finding.Fix);
        Assert.Contains("not supported on temporary tables", finding.Fix);
        Assert.Contains("ALTER INDEX ALL ON #Staging REBUILD;", finding.Fix);
    }

    // ---- MSSQL-REV-001 ------------------------------------------------------------------

    [Theory]
    [InlineData("DELETE FROM dbo.SessionCache;")]
    [InlineData("TRUNCATE TABLE dbo.SessionCache;")]
    public void Backup_copy_fix_is_collision_proof_and_labelled_data_only(string sql)
    {
        var report = Analyze(sql);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-REV-001");
        Assert.NotNull(finding.Fix);

        // A fixed _backup suffix fails with 2714 the second time the migration runs.
        Assert.Contains("SELECT * INTO dbo.SessionCache_backup_<yyyymmdd> FROM dbo.SessionCache;", finding.Fix);
        Assert.DoesNotContain("INTO dbo.SessionCache_backup FROM", finding.Fix);
        Assert.Contains("error 2714", finding.Fix);

        // The copy carries data only; indexes/constraints/triggers stay on the source by design.
        Assert.Contains("Data-only copy", finding.Fix);
        Assert.Contains("no indexes, constraints or triggers", finding.Fix);
    }

    [Fact]
    public void Backup_copy_fix_keeps_the_next_step_of_each_case()
    {
        var deleteFix = Assert.Single(
            Analyze("DELETE FROM dbo.SessionCache;").Findings, f => f.RuleId == "MSSQL-REV-001").Fix;
        var truncateFix = Assert.Single(
            Analyze("TRUNCATE TABLE dbo.SessionCache;").Findings, f => f.RuleId == "MSSQL-REV-001").Fix;

        Assert.EndsWith("-- Verify the copy, then delete in batches with a WHERE clause.", deleteFix);
        Assert.EndsWith("-- Verify the copy, then truncate.", truncateFix);
    }
}
