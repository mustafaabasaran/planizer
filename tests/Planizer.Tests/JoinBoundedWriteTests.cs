using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

/// <summary>
/// A join in the FROM clause of a WHERE-less UPDATE/DELETE used to count as a filter by its mere
/// presence, which silenced MSSQL-LOCK-009, MSSQL-REV-001 and MSSQL-REV-002 on statements that
/// wipe the whole table (<c>DELETE t FROM dbo.Orders t LEFT JOIN …</c>). These pin the three-state
/// replacement: the join filters only when it can actually drop rows of the target.
/// </summary>
public class JoinBoundedWriteTests
{
    private static Report Analyze(string sql)
        => new MsSqlAnalyzer().Analyze([("m.sql", sql)], new PlanizerConfig { Rollback = true });

    private static Finding Lock009(string sql)
        => Assert.Single(Analyze(sql).Findings, f => f.RuleId == "MSSQL-LOCK-009");

    [Theory]
    // The target is the preserved side of an outer join: every one of its rows survives the join.
    [InlineData("DELETE t FROM dbo.Orders t LEFT JOIN dbo.Customers c ON c.Id = t.CustomerId;", "LEFT JOIN")]
    [InlineData("DELETE t FROM dbo.Customers c RIGHT JOIN dbo.Orders t ON t.CustomerId = c.Id;", "RIGHT JOIN")]
    [InlineData("DELETE t FROM dbo.Orders t FULL OUTER JOIN dbo.Customers c ON c.Id = t.CustomerId;", "FULL OUTER JOIN")]
    // A cross join pairs every row of both sides, whichever way it is written.
    [InlineData("DELETE t FROM dbo.Orders t CROSS JOIN dbo.Customers c;", "CROSS JOIN")]
    [InlineData("DELETE t FROM dbo.Orders t, dbo.Customers c;", "comma cross join")]
    // OUTER APPLY keeps the left row even when the right side returns nothing.
    [InlineData("DELETE t FROM dbo.Orders t OUTER APPLY (SELECT TOP (1) c.Id AS Id FROM dbo.Customers c WHERE c.Id = t.CustomerId) x;", "OUTER APPLY")]
    public void A_join_that_cannot_drop_target_rows_is_not_a_filter(string sql, string join)
    {
        var report = Analyze(sql);

        var locking = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LOCK-009");
        Assert.Equal(Severity.Warning, locking.Severity);
        Assert.False(locking.Inconclusive);
        Assert.Contains($"the {join} does not restrict dbo.Orders", locking.Message);

        var irreversible = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-REV-001");
        Assert.Equal(Severity.Critical, irreversible.Severity);
        Assert.Contains($"The {join} does not restrict dbo.Orders; every row is deleted", irreversible.Message);

        // The join-aware message and the date-stamped, data-only backup fix arrived on separate
        // branches and meet only here; a fixed _backup target would fail with 2714 on a re-run.
        Assert.Contains("SELECT * INTO dbo.Orders_backup_<yyyymmdd> FROM dbo.Orders;", irreversible.Fix);
        Assert.DoesNotContain("INTO dbo.Orders_backup FROM", irreversible.Fix);

        // REV-002 mirrors REV-001's trigger set, so it must not double-flag the same statement.
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSSQL-REV-002");
    }

    [Theory]
    // The target is the null-supplying side: filtered exactly like an inner join, so whether every
    // row matches is a data question — same Inconclusive verdict, no Critical on a guess.
    [InlineData("DELETE t FROM dbo.Customers c LEFT JOIN dbo.Orders t ON t.CustomerId = c.Id;", "LEFT JOIN")]
    [InlineData("DELETE t FROM dbo.Orders t RIGHT JOIN dbo.Customers c ON c.Id = t.CustomerId;", "RIGHT JOIN")]
    [InlineData("UPDATE t SET t.Archived = 1 FROM dbo.Customers c LEFT JOIN dbo.Orders t ON t.CustomerId = c.Id;", "LEFT JOIN")]
    public void The_null_supplying_side_of_an_outer_join_is_inconclusive_like_an_inner_join(string sql, string join)
    {
        var report = Analyze(sql);

        var locking = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LOCK-009");
        Assert.Equal(Severity.Info, locking.Severity);
        Assert.True(locking.Inconclusive);
        Assert.Contains($"cardinality of the {join}", locking.Message);
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSSQL-REV-001");
    }

    [Theory]
    // A comma list must not override the verdict of the reference that actually holds the target:
    // the other references cross join against it and can never resurrect dropped rows.
    [InlineData("DELETE t FROM dbo.X x, dbo.A a LEFT JOIN dbo.B t ON a.Id = t.Id;", "LEFT JOIN")]
    [InlineData("DELETE t FROM dbo.X x, dbo.A a INNER JOIN dbo.B t ON a.Id = t.Id;", "INNER JOIN")]
    public void A_comma_list_defers_to_the_join_holding_the_target(string sql, string join)
    {
        var report = Analyze(sql);

        var locking = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LOCK-009");
        Assert.Equal(Severity.Info, locking.Severity);
        Assert.True(locking.Inconclusive);
        Assert.Contains($"cardinality of the {join}", locking.Message);
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSSQL-REV-001");
    }

    [Fact]
    public void A_comma_list_with_the_target_on_a_preserved_side_names_that_join()
    {
        var finding = Lock009("DELETE t FROM dbo.X x, dbo.Orders t LEFT JOIN dbo.Customers c ON c.Id = t.CustomerId;");

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("the LEFT JOIN does not restrict dbo.Orders", finding.Message);
    }

    [Theory]
    // Whether these drop target rows depends on the data, not on the syntax.
    [InlineData("DELETE t FROM dbo.Orders t INNER JOIN dbo.Customers c ON c.Id = t.CustomerId;", "INNER JOIN")]
    [InlineData("DELETE t FROM dbo.Orders t CROSS APPLY (SELECT TOP (1) c.Id AS Id FROM dbo.Customers c WHERE c.Id = t.CustomerId) x;", "CROSS APPLY")]
    public void An_undecidable_join_reports_info_rather_than_staying_silent(string sql, string join)
    {
        var report = Analyze(sql);

        var locking = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LOCK-009");
        Assert.Equal(Severity.Info, locking.Severity);
        Assert.True(locking.Inconclusive);
        Assert.Contains($"depends on the cardinality of the {join}", locking.Message);
        Assert.Contains("A schema snapshot settles this.", locking.Message);

        // Critical on a guess would be wrong: the data-loss rule stays out of it, and the missing
        // inverse is left to REV-002's per-file DML summary.
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSSQL-REV-001");
        Assert.Contains(report.Findings, f => f.RuleId == "MSSQL-REV-002");
    }

    [Fact]
    public void The_strongest_join_on_the_path_to_the_target_wins()
    {
        // dbo.Orders is preserved by the LEFT JOIN but restricted — maybe — by the INNER JOIN.
        var finding = Lock009(
            """
            DELETE t
            FROM dbo.Orders t
            INNER JOIN dbo.Customers c ON c.Id = t.CustomerId
            LEFT JOIN dbo.Regions r ON r.Id = c.RegionId;
            """);

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Contains("cardinality of the INNER JOIN", finding.Message);
    }

    [Fact]
    public void A_parenthesised_join_is_seen_through()
    {
        var finding = Lock009("DELETE t FROM (dbo.Orders t LEFT JOIN dbo.Customers c ON c.Id = t.CustomerId);");

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("the LEFT JOIN does not restrict dbo.Orders", finding.Message);
    }

    [Fact]
    public void A_target_missing_from_the_from_clause_is_cross_joined_in()
    {
        // T-SQL joins dbo.A to the FROM result as a cross join, so every row of dbo.A is updated —
        // and no join in the clause has anything to do with it, so the message names none.
        var finding = Lock009("UPDATE dbo.A SET Flag = 1 FROM dbo.B b JOIN dbo.C c ON c.Id = b.Id;");

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(
            "UPDATE on dbo.A has no WHERE and no TOP: it touches every row, and after ~5000 row " +
            "locks lock escalation turns it into a table lock.",
            finding.Message);
    }

    [Fact]
    public void A_second_from_clause_naming_the_target_resolves_to_the_joined_leaf()
    {
        var finding = Lock009("DELETE FROM dbo.Orders FROM dbo.Orders o LEFT JOIN dbo.Customers c ON c.Id = o.CustomerId;");

        Assert.Contains("the LEFT JOIN does not restrict dbo.Orders", finding.Message);
    }

    [Fact]
    public void An_aliased_table_variable_target_stays_transient_across_a_join()
    {
        // @Ids is session-scoped: it escalates no locks on user tables and holds nothing to
        // restore, so neither the cross join nor the alias may drag it into a finding.
        const string sql = """
            DECLARE @Ids TABLE (Id bigint NOT NULL);
            DELETE i FROM @Ids i CROSS JOIN dbo.Customers c;
            """;

        var report = Analyze(sql);

        Assert.DoesNotContain(report.Findings,
            f => f.RuleId is "MSSQL-LOCK-009" or "MSSQL-REV-001" or "MSSQL-REV-002");
    }

    [Fact]
    public void A_where_clause_still_bounds_the_write_whatever_the_join_is()
    {
        var report = Analyze("DELETE t FROM dbo.Orders t CROSS JOIN dbo.Customers c WHERE t.Id < 100;");

        Assert.DoesNotContain(report.Findings, f => f.RuleId is "MSSQL-LOCK-009" or "MSSQL-REV-001");
    }
}
