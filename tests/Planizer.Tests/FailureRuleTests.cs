using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

/// <summary>
/// Exact-wording and aggregation checks for the failure-risk family (MSSQL-BATCH-001/002,
/// MSSQL-LIT-001). Fixtures assert rule/severity/line; the message and fix texts live here.
/// </summary>
public class FailureRuleTests
{
    private static Report Analyze(string sql, PlanizerConfig? config = null)
        => new MsSqlAnalyzer().Analyze([("m.sql", sql)], config ?? new PlanizerConfig());

    // --- MSSQL-BATCH-001 ---

    [Fact]
    public void New_column_reference_names_the_column_the_origin_line_and_both_fixes()
    {
        const string sql = """
            ALTER TABLE dbo.Orders ADD Status tinyint NULL;
            UPDATE dbo.Orders SET Status = 3 WHERE ShippedDate IS NOT NULL;
            """;

        var report = Analyze(sql);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-BATCH-001");
        Assert.Equal(Severity.Blocker, finding.Severity);
        Assert.Equal(2, finding.Location.Line);
        Assert.Contains("column dbo.Orders.Status (added at line 1)", finding.Message);
        Assert.Contains("error 207 (Invalid column name 'Status')", finding.Message);
        Assert.Contains("Put GO after line 1", finding.Fix);
        Assert.Contains("EXEC sp_executesql N'UPDATE dbo.Orders SET Status = 3 WHERE ShippedDate IS NOT NULL;';", finding.Fix);
    }

    [Fact]
    public void Renamed_column_reference_says_renamed_and_resolves_bracketed_names()
    {
        const string sql = """
            EXEC sp_rename N'[dbo].[Customers].[Fax]', N'FaxNumber', N'COLUMN';
            SELECT Customers.FaxNumber FROM dbo.Customers;
            """;

        var report = Analyze(sql);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-BATCH-001");
        Assert.Contains("dbo.Customers.FaxNumber (renamed at line 1)", finding.Message);
    }

    [Fact]
    public void Two_new_columns_in_one_statement_produce_one_finding_listing_both()
    {
        const string sql = """
            ALTER TABLE dbo.Orders ADD Status tinyint NULL, Note nvarchar(50) NULL;
            UPDATE dbo.Orders SET Status = 1, Note = N'x' WHERE Id = 1;
            """;

        var report = Analyze(sql);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-BATCH-001");
        Assert.Contains("columns dbo.Orders.Status (added at line 1), dbo.Orders.Note (added at line 1)", finding.Message);
        Assert.Contains("columns do not exist yet", finding.Message);
    }

    [Fact]
    public void Unqualified_reference_in_a_statement_that_never_names_the_table_is_not_a_match()
    {
        const string sql = """
            ALTER TABLE dbo.Orders ADD Status tinyint NULL;
            SELECT Status FROM dbo.Customers WHERE Id = 1;
            """;

        Assert.DoesNotContain(Analyze(sql).Findings, f => f.RuleId == "MSSQL-BATCH-001");
    }

    [Fact]
    public void Unqualified_table_name_and_dbo_prefixed_name_are_the_same_table()
    {
        const string sql = """
            ALTER TABLE [Orders] ADD [Status] tinyint NULL;
            UPDATE dbo.Orders SET Status = 1 WHERE Id = 1;
            """;

        Assert.Single(Analyze(sql).Findings, f => f.RuleId == "MSSQL-BATCH-001");
    }

    [Fact]
    public void Reference_in_a_while_predicate_counts_but_the_loop_body_is_not_double_counted()
    {
        const string sql = """
            ALTER TABLE dbo.Orders ADD Status tinyint NULL;
            WHILE EXISTS (SELECT 1 FROM dbo.Orders WHERE Status IS NULL)
            BEGIN
                UPDATE TOP (1000) dbo.Orders SET Status = 0 WHERE Status IS NULL;
            END
            """;

        var report = Analyze(sql);

        var findings = report.Findings.Where(f => f.RuleId == "MSSQL-BATCH-001").ToList();
        Assert.Equal(2, findings.Count);
        Assert.Equal([2, 4], findings.Select(f => f.Location.Line).OrderBy(l => l));
    }

    [Fact]
    public void Guarded_add_says_the_failure_is_environment_dependent()
    {
        const string sql = """
            IF COL_LENGTH('dbo.Orders', 'Status') IS NULL
            BEGIN
                ALTER TABLE dbo.Orders ADD Status tinyint NULL;
            END
            UPDATE dbo.Orders SET Status = 3 WHERE ShippedDate IS NOT NULL;
            """;

        var finding = Assert.Single(Analyze(sql).Findings, f => f.RuleId == "MSSQL-BATCH-001");
        Assert.Equal(Severity.Blocker, finding.Severity);
        Assert.EndsWith(
            "(Invalid column name 'Status'). The statement introducing it (line 3) is guarded by a catalog " +
            "check, so the batch fails on any database where the column does not exist yet (a fresh " +
            "environment or a first deployment); it only compiles where an earlier run already added the column.",
            finding.Message);
    }

    [Fact]
    public void Exit_guard_before_the_add_counts_as_guarded_too()
    {
        const string sql = """
            IF COL_LENGTH('dbo.Orders', 'Status') IS NOT NULL RETURN;
            ALTER TABLE dbo.Orders ADD Status tinyint NULL;
            UPDATE dbo.Orders SET Status = 0 WHERE Status IS NULL;
            """;

        var finding = Assert.Single(Analyze(sql).Findings, f => f.RuleId == "MSSQL-BATCH-001");
        Assert.Contains("The statement introducing it (line 2) is guarded by a catalog check", finding.Message);
    }

    [Fact]
    public void Unguarded_add_does_not_mention_environment_dependence()
    {
        const string sql = """
            ALTER TABLE dbo.Orders ADD Status tinyint NULL;
            UPDATE dbo.Orders SET Status = 3 WHERE ShippedDate IS NOT NULL;
            """;

        var finding = Assert.Single(Analyze(sql).Findings, f => f.RuleId == "MSSQL-BATCH-001");
        Assert.EndsWith("(Invalid column name 'Status').", finding.Message);
        Assert.DoesNotContain("guarded", finding.Message);
    }

    // --- MSSQL-BATCH-002 ---

    [Fact]
    public void Scalar_variable_across_go_reports_error_137_and_a_copyable_redeclaration()
    {
        const string sql = """
            DECLARE @tenant int = 1;
            GO
            DELETE FROM dbo.Cache WHERE TenantId = @tenant;
            """;

        var report = Analyze(sql);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-BATCH-002");
        Assert.Equal(Severity.Blocker, finding.Severity);
        Assert.Equal(3, finding.Location.Line);
        Assert.Equal(
            "@tenant is used here but was declared at line 1 in an earlier batch; GO ends the scope of every variable, " +
            "so this batch fails to compile with error 137 (Must declare the scalar variable \"@tenant\").",
            finding.Message);
        Assert.Equal("Re-declare in this batch, before the first use:\nDECLARE @tenant int = 1;", finding.Fix);
    }

    [Fact]
    public void Table_variable_across_go_reports_error_1087_and_repeats_the_declaration()
    {
        const string sql = """
            DECLARE @ids TABLE (Id int NOT NULL)
            GO
            SELECT Id FROM @ids;
            """;

        var report = Analyze(sql);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-BATCH-002");
        Assert.Contains("error 1087 (Must declare the table variable \"@ids\")", finding.Message);
        Assert.EndsWith("DECLARE @ids TABLE (Id int NOT NULL);", finding.Fix);
    }

    [Fact]
    public void Several_variables_in_one_statement_produce_one_finding()
    {
        const string sql = """
            DECLARE @from int = 1, @to int = 2;
            GO
            UPDATE dbo.T SET Id = @to WHERE Id = @from;
            """;

        var report = Analyze(sql);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-BATCH-002");
        Assert.StartsWith("Variables @to (declared at line 1), @from (declared at line 1) are used here", finding.Message);
        Assert.Contains("DECLARE @to int = 2;", finding.Fix);
        Assert.Contains("DECLARE @from int = 1;", finding.Fix);
    }

    [Fact]
    public void Variable_never_declared_anywhere_is_not_this_rule()
    {
        Assert.DoesNotContain(Analyze("SELECT @nowhere;").Findings, f => f.RuleId == "MSSQL-BATCH-002");
    }

    [Fact]
    public void Return_value_capture_and_output_arguments_are_uses()
    {
        const string sql = """
            DECLARE @rc int, @out int;
            GO
            EXEC @rc = dbo.P @result = @out OUTPUT;
            """;

        var report = Analyze(sql);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-BATCH-002");
        Assert.Contains("@rc", finding.Message);
        Assert.Contains("@out", finding.Message);
        Assert.DoesNotContain("@result", finding.Message);
    }

    [Fact]
    public void Batches_are_scoped_per_file()
    {
        var report = new MsSqlAnalyzer().Analyze(
            [("a.sql", "DECLARE @x int = 1;\nSELECT @x;"), ("b.sql", "SELECT @x;")],
            new PlanizerConfig());

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSSQL-BATCH-002");
    }

    // --- MSSQL-LIT-001 ---

    [Fact]
    public void Non_unicode_literals_are_summarised_once_per_file_with_examples()
    {
        const string sql = """
            INSERT INTO dbo.PaymentType (Code, Name) VALUES ('CASH', 'Nakit Ödeme');
            INSERT INTO dbo.PaymentType (Code, Name) VALUES ('CARD', 'Kredi Kartı');
            UPDATE dbo.PaymentType SET Description = N'Açıklama' WHERE Code = 'CASH';
            DELETE FROM dbo.PaymentType WHERE Name = 'Çek';
            """;

        var report = Analyze(sql);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LIT-001");
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(1, finding.Location.Line);
        Assert.StartsWith(
            "3 string literals in this file contain non-ASCII characters without the N prefix " +
            "(first: 'Nakit Ödeme' at line 1; also 'Kredi Kartı', 'Çek').",
            finding.Message);
        Assert.Equal("Prefix the literals with N: N'Nakit Ödeme' (and the 2 others)", finding.Fix);
    }

    [Fact]
    public void Single_non_unicode_literal_uses_singular_wording()
    {
        var report = Analyze("UPDATE dbo.T SET Name = 'Şube' WHERE Id = 1;");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LIT-001");
        Assert.StartsWith("1 string literal in this file contains non-ASCII characters without the N prefix: 'Şube' (line 1).", finding.Message);
        Assert.Equal("Prefix the literal with N: N'Şube'", finding.Fix);
    }

    [Fact]
    public void Suppressed_statements_leave_the_count_and_the_anchor_moves()
    {
        const string sql = """
            -- planizer:ignore MSSQL-LIT-001 column is varchar on purpose
            INSERT INTO dbo.T (Name) VALUES ('Şube');
            INSERT INTO dbo.T (Name) VALUES ('Ödeme');
            """;

        var report = Analyze(sql);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LIT-001");
        Assert.False(finding.Suppressed);
        Assert.Equal(3, finding.Location.Line);
        Assert.StartsWith("1 string literal", finding.Message);
    }

    [Fact]
    public void Fully_suppressed_file_produces_no_literal_finding()
    {
        const string sql = """
            -- planizer:ignore MSSQL-LIT-001 column is varchar on purpose
            INSERT INTO dbo.T (Name) VALUES ('Şube');
            """;

        Assert.DoesNotContain(Analyze(sql).Findings, f => f.RuleId == "MSSQL-LIT-001");
    }

    [Fact]
    public void Long_literal_examples_are_truncated()
    {
        var longValue = "Ç" + new string('a', 60);
        var report = Analyze($"UPDATE dbo.T SET Name = '{longValue}' WHERE Id = 1;");

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LIT-001");
        Assert.DoesNotContain(longValue, finding.Message);
        Assert.Contains("'Ç" + new string('a', 38) + "…'", finding.Message);
    }

    [Fact]
    public void Message_text_in_print_raiserror_and_throw_is_not_counted()
    {
        const string sql = """
            PRINT 'Ödeme türleri yükleniyor';
            RAISERROR('Adım 1 başladı', 0, 1) WITH NOWAIT;
            INSERT INTO dbo.PaymentType (Code, Name) VALUES ('CASH', 'Nakit Ödeme');
            IF @@ERROR <> 0 THROW 50001, 'Yükleme başarısız', 1;
            """;

        var report = Analyze(sql);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-LIT-001");
        Assert.Equal(3, finding.Location.Line);
        Assert.StartsWith(
            "1 string literal in this file contains non-ASCII characters without the N prefix: 'Nakit Ödeme' (line 3).",
            finding.Message);

        var messagesOnly = Analyze("PRINT 'Başladı';\nRAISERROR('Bitti: Ödeme', 0, 1) WITH NOWAIT;");
        Assert.DoesNotContain(messagesOnly.Findings, f => f.RuleId == "MSSQL-LIT-001");
    }

    // --- MSSQL-LIM-002 ---

    [Fact]
    public void Global_temp_table_names_get_the_full_128_characters()
    {
        var local = $"CREATE TABLE #{new string('L', 116)} (Id int NOT NULL);";   // 117 characters with '#'
        var global = $"CREATE TABLE ##{new string('G', 126)} (Id int NOT NULL);"; // 128 characters with '##'
        var tooLong = $"CREATE TABLE ##{new string('G', 127)} (Id int NOT NULL);"; // 129

        var finding = Assert.Single(Analyze(local).Findings, f => f.RuleId == "MSSQL-LIM-002");
        Assert.StartsWith("Temporary table name", finding.Message);
        Assert.Contains("at most 116", finding.Message);

        Assert.DoesNotContain(Analyze(global).Findings, f => f.RuleId == "MSSQL-LIM-002");

        // Past 128 the grammar itself refuses the name (error 46095): PARSE-001, never LIM-002.
        var overrun = Analyze(tooLong).Findings;
        Assert.Single(overrun, f => f.RuleId == MsSqlAnalyzer.ParseRuleId);
        Assert.DoesNotContain(overrun, f => f.RuleId == "MSSQL-LIM-002");
    }

}
