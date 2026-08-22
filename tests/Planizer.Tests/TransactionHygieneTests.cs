using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql;
using Planizer.MsSql.Parsing;

namespace Planizer.Tests;

/// <summary>
/// Invariants of the MSSQL-TRAN rules and <see cref="TransactionPaths"/> that fixtures cannot
/// express (counts, exact wording, path bookkeeping).
/// </summary>
public class TransactionHygieneTests
{
    private static Report Analyze(string sql, string file = "m.sql")
        => new MsSqlAnalyzer().Analyze([(file, sql)], new PlanizerConfig());

    private static TransactionPaths Paths(string sql)
    {
        var result = new MsSqlScriptParser().Parse(sql, "m.sql", SqlServerVersion.Sql2019);
        Assert.Empty(result.Errors);
        return TransactionPaths.Build(result.Statements);
    }

    [Fact]
    public void Tran001_reports_once_per_file_anchored_to_the_first_begin_tran()
    {
        var report = Analyze("""
            BEGIN TRAN;
            ALTER TABLE dbo.A ADD C1 int NULL;
            COMMIT;
            BEGIN TRAN;
            ALTER TABLE dbo.B ADD C2 int NULL;
            COMMIT;
            """);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-TRAN-001");
        Assert.Equal(1, finding.Location.Line);
        Assert.Contains("SET XACT_ABORT ON;", finding.Fix);
    }

    [Fact]
    public void Tran001_is_evaluated_per_file()
    {
        var report = new MsSqlAnalyzer().Analyze(
        [
            ("a.sql", "SET XACT_ABORT ON;\nBEGIN TRAN;\nDELETE FROM dbo.A WHERE Id = 1;\nCOMMIT;"),
            ("b.sql", "BEGIN TRAN;\nDELETE FROM dbo.B WHERE Id = 1;\nCOMMIT;"),
        ], new PlanizerConfig());

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-TRAN-001");
        Assert.Equal("b.sql", finding.Location.File);
    }

    [Fact]
    public void Tran002_unmatched_commit_and_rollback_name_their_error_numbers()
    {
        var commit = Assert.Single(Analyze("COMMIT;").Findings, f => f.RuleId == "MSSQL-TRAN-002");
        Assert.Contains("error 3902", commit.Message);
        Assert.Contains("IF @@TRANCOUNT > 0 COMMIT;", commit.Fix);

        var rollback = Assert.Single(Analyze("ROLLBACK;").Findings, f => f.RuleId == "MSSQL-TRAN-002");
        Assert.Contains("error 3903", rollback.Message);
    }

    [Fact]
    public void Tran002_reports_each_transaction_left_open()
    {
        var report = Analyze("""
            BEGIN TRAN;
            DELETE FROM dbo.A WHERE Id = 1;
            GO
            BEGIN TRAN;
            DELETE FROM dbo.B WHERE Id = 1;
            """);

        var lines = report.Findings.Where(f => f.RuleId == "MSSQL-TRAN-002").Select(f => f.Location.Line);
        Assert.Equal([1, 4], lines); // in opening order
    }

    [Fact]
    public void Tran003_does_not_fire_when_begin_and_commit_share_a_batch()
    {
        var report = Analyze("BEGIN TRAN;\nDELETE FROM dbo.A WHERE Id = 1;\nCOMMIT;\nGO\nSELECT 1;");

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSSQL-TRAN-003");
    }

    [Fact]
    public void Tran003_message_counts_the_batches_spanned()
    {
        var report = Analyze("BEGIN TRAN;\nGO\nSELECT 1;\nGO\nCOMMIT;");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-TRAN-003");
        Assert.Contains("committed at line 5, 2 GO batches later", finding.Message);
    }

    [Fact]
    public void Tran003_reports_up_to_five_spanning_transactions_one_by_one()
    {
        var report = Analyze(SpanningTransactions(5));

        var findings = report.Findings.Where(f => f.RuleId == "MSSQL-TRAN-003").ToList();
        Assert.Equal([1, 7, 13, 19, 25], findings.Select(f => f.Location.Line));
        Assert.All(findings, f => Assert.StartsWith("The transaction opened at line", f.Message));
    }

    [Fact]
    public void Tran003_aggregates_per_file_above_five_spanning_transactions()
    {
        var report = Analyze(SpanningTransactions(6));

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-TRAN-003");
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(1, finding.Location.Line);
        Assert.Equal(
            "6 transactions in this file are opened in one GO batch and closed in a later one " +
            "(first: line 1, committed at line 5, 2 GO batches later; also lines 7, 13, 19, …); " +
            "if a batch in between fails, that transaction stays open and the remaining batches run inside it.",
            finding.Message);
        Assert.Contains("--no-transactions", finding.Fix);
    }

    [Fact]
    public void Tran003_suppressed_transactions_leave_the_aggregate_count()
    {
        const string ignore = "-- planizer:ignore MSSQL-TRAN-003 runner re-opens on failure\n";

        // 6 spanning, 1 suppressed: 5 counted, so every one is reported and the first is marked suppressed.
        var six = Analyze(ignore + SpanningTransactions(6)).Findings.Where(f => f.RuleId == "MSSQL-TRAN-003").ToList();
        Assert.Equal(6, six.Count);
        Assert.Equal(2, Assert.Single(six, f => f.Suppressed).Location.Line);

        // 7 spanning, 1 suppressed: 6 counted, one aggregate anchored at the first unsuppressed BEGIN TRAN.
        var seven = Assert.Single(Analyze(ignore + SpanningTransactions(7)).Findings, f => f.RuleId == "MSSQL-TRAN-003");
        Assert.False(seven.Suppressed);
        Assert.Equal(8, seven.Location.Line);
        Assert.StartsWith("6 transactions in this file", seven.Message);
    }

    /// <summary>EF Core idempotent shape, 6 lines per block: BEGIN TRANSACTION at lines 1, 7, 13, …</summary>
    private static string SpanningTransactions(int count)
        => string.Concat(Enumerable.Range(1, count).Select(i =>
            $"BEGIN TRANSACTION;\nGO\nALTER TABLE dbo.A ADD C{i} int NULL;\nGO\nCOMMIT;\nGO\n"));

    [Fact]
    public void Tran006_counts_only_statements_that_do_work()
    {
        var body = string.Join('\n', Enumerable.Range(1, 24).Select(i =>
            $"IF NOT EXISTS (SELECT 1 FROM dbo.L WHERE Id = {i}) INSERT INTO dbo.L (Id) VALUES ({i});"));

        // 24 IFs + 24 INSERTs + SET/DECLARE/PRINT = well over 25 flattened statements, 24 working ones.
        var below = Analyze($"BEGIN TRAN;\nSET NOCOUNT ON;\nDECLARE @x int;\nPRINT 'x';\n{body}\nCOMMIT;");
        Assert.DoesNotContain(below.Findings, f => f.RuleId == "MSSQL-TRAN-006");

        var at = Analyze($"BEGIN TRAN;\n{body}\nINSERT INTO dbo.L (Id) VALUES (25);\nCOMMIT;");
        var finding = Assert.Single(at.Findings, f => f.RuleId == "MSSQL-TRAN-006");
        Assert.Contains("wraps 25 statements", finding.Message);
        Assert.Equal(1, finding.Location.Line);
    }

    [Fact]
    public void Paths_pair_nested_begins_with_their_commits()
    {
        var paths = Paths("BEGIN TRAN;\nBEGIN TRAN;\nSELECT 1;\nCOMMIT;\nCOMMIT;");

        Assert.Equal(2, paths.Closed.Count);
        Assert.Equal((2, 4), (paths.Closed[0].Begin.Location.Line, paths.Closed[0].End.Location.Line));
        Assert.Equal((1, 5), (paths.Closed[1].Begin.Location.Line, paths.Closed[1].End.Location.Line));
        Assert.Empty(paths.LeftOpen);
        Assert.Empty(paths.Unmatched);
    }

    [Fact]
    public void Paths_rollback_closes_every_open_level()
    {
        var paths = Paths("BEGIN TRAN;\nBEGIN TRAN;\nSELECT 1;\nROLLBACK;\nCOMMIT;");

        Assert.Equal(2, paths.Closed.Count);
        Assert.All(paths.Closed, c => Assert.Equal(4, c.End.Location.Line));
        var stray = Assert.Single(paths.Unmatched);
        Assert.Equal(5, stray.Location.Line);
    }

    [Fact]
    public void Paths_ignore_rollback_to_a_savepoint()
    {
        var paths = Paths("BEGIN TRAN;\nSAVE TRAN sp1;\nSELECT 1;\nROLLBACK TRAN sp1;\nCOMMIT;");

        var closed = Assert.Single(paths.Closed);
        Assert.IsType<CommitTransactionStatement>(closed.End.Ast);
        Assert.Empty(paths.Unmatched);
    }

    [Fact]
    public void Paths_treat_catch_as_the_error_path()
    {
        var paths = Paths("""
            BEGIN TRY
                BEGIN TRAN;
                SELECT 1;
            END TRY
            BEGIN CATCH
                ROLLBACK;
            END CATCH
            """);

        var open = Assert.Single(paths.LeftOpen);
        Assert.Equal(2, open.Location.Line);
        Assert.Empty(paths.Closed);
    }

    [Fact]
    public void Paths_continue_with_the_branch_that_changed_the_state()
    {
        var paths = Paths("""
            DECLARE @t bit = 1;
            IF @t = 1 BEGIN TRAN;
            SELECT 1;
            IF @t = 1 COMMIT;
            """);

        Assert.Single(paths.Closed);
        Assert.Empty(paths.LeftOpen);
        Assert.Empty(paths.Unmatched);
    }

    [Fact]
    public void Paths_do_not_continue_a_branch_that_returns()
    {
        var paths = Paths("""
            BEGIN TRAN;
            IF @@ERROR <> 0 BEGIN ROLLBACK; RETURN; END
            COMMIT;
            """);

        Assert.Equal(2, paths.Closed.Count); // the ROLLBACK on the error branch and the COMMIT on the main path
        Assert.Empty(paths.LeftOpen);
        Assert.Empty(paths.Unmatched);
    }

    [Theory]
    [InlineData("IF @@TRANCOUNT > 0 COMMIT;", true)]
    [InlineData("IF XACT_STATE() <> 0 ROLLBACK;", true)]
    [InlineData("IF @@TRANCOUNT > 0 PRINT 'x'; ELSE COMMIT;", false)]
    [InlineData("IF @@ERROR <> 0 COMMIT;", false)]
    [InlineData("IF @@TRANCOUNT > 0 BEGIN IF 1 = 1 COMMIT; END", true)]
    public void Paths_recognise_trancount_guards(string sql, bool guarded)
    {
        var paths = Paths(sql);
        var closing = paths.Statements.Single(s => s.Ast is CommitTransactionStatement or RollbackTransactionStatement);

        Assert.Equal(guarded, TransactionPaths.IsTranCountGuarded(closing));
        Assert.Equal(guarded, paths.Unmatched.Count == 0);
    }

    [Theory]
    [InlineData("THROW;", true)]
    [InlineData("RAISERROR('x', 16, 1);", true)]
    [InlineData("RAISERROR('x', 10, 1);", false)]
    [InlineData("DECLARE @s int = ERROR_SEVERITY(); RAISERROR('x', @s, 1);", true)]
    [InlineData("EXEC dbo.usp_RethrowError;", true)]
    [InlineData("EXEC dbo.usp_LogError;", false)]
    [InlineData("PRINT ERROR_MESSAGE();", false)]
    public void Paths_classify_catch_bodies_that_rethrow(string catchBody, bool rethrows)
    {
        var paths = Paths($"BEGIN TRY\n    SELECT 1;\nEND TRY\nBEGIN CATCH\n    {catchBody}\nEND CATCH");
        var tryCatch = paths.Statements.Single(s => s.Ast is TryCatchStatement);

        Assert.Equal(rethrows, TransactionPaths.Rethrows(paths.CatchBody(tryCatch)));
    }

    [Fact]
    public void Paths_stop_at_a_top_level_return_so_a_label_handler_is_the_error_path()
    {
        var paths = Paths("""
            BEGIN TRAN;
            UPDATE dbo.A SET C1 = 1 WHERE Id = 1;
            IF @@ERROR <> 0 GOTO ERR;
            COMMIT;
            RETURN;
            ERR:
            ROLLBACK;
            """);

        var closed = Assert.Single(paths.Closed);
        Assert.IsType<CommitTransactionStatement>(closed.End.Ast);
        Assert.Empty(paths.LeftOpen);
        Assert.Empty(paths.Unmatched);
    }

    [Fact]
    public void Paths_follow_a_forward_goto_to_its_label()
    {
        var paths = Paths("""
            GOTO Skip;
            BEGIN TRAN;
            Skip:
            BEGIN TRAN;
            COMMIT;
            """);

        Assert.Single(paths.Closed);
        Assert.Empty(paths.LeftOpen); // the BEGIN TRAN jumped over never opens
        Assert.Empty(paths.Unmatched);
    }

    [Fact]
    public void Paths_treat_a_while_trancount_loop_as_a_guard()
    {
        var paths = Paths("""
            WHILE @@TRANCOUNT > 0 ROLLBACK;
            BEGIN TRAN;
            COMMIT;
            """);

        var rollback = paths.Statements.Single(s => s.Ast is RollbackTransactionStatement);
        Assert.True(TransactionPaths.IsTranCountGuarded(rollback));
        Assert.Empty(paths.Unmatched);
    }

    [Fact]
    public void Paths_know_which_rollbacks_only_reach_a_savepoint()
    {
        var paths = Paths("""
            BEGIN TRAN;
            SAVE TRANSACTION sp1;
            ROLLBACK TRANSACTION sp1;
            ROLLBACK;
            """);

        var rollbacks = paths.Statements.Where(s => s.Ast is RollbackTransactionStatement).ToList();
        Assert.Contains("sp1", paths.SavepointNames);
        Assert.True(paths.IsRollbackToSavepoint(rollbacks[0]));
        Assert.False(paths.IsRollbackToSavepoint(rollbacks[1]));
    }

    [Fact]
    public void Tran004_savepoint_rollback_in_catch_does_not_satisfy_the_rule_and_is_named()
    {
        var report = Analyze("""
            BEGIN TRY
                BEGIN TRAN;
                SAVE TRANSACTION sp1;
                UPDATE dbo.A SET C1 = 1 WHERE Id = 1;
                COMMIT;
            END TRY
            BEGIN CATCH
                ROLLBACK TRANSACTION sp1;
                THROW;
            END CATCH
            """);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-TRAN-004");
        Assert.Equal(2, finding.Location.Line);
        Assert.Contains("only rolled back to savepoint sp1 in its CATCH block (line 8)", finding.Message);
        Assert.Contains("error 3931", finding.Message);
    }

}
