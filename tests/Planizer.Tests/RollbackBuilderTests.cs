using Planizer.Core;
using Planizer.MsSql;
using Planizer.MsSql.Rules.Reversibility;

namespace Planizer.Tests;

/// <summary>
/// Tests for <see cref="RollbackScriptBuilder"/>: the inverse pairs from the plan inventory
/// (ADD COLUMN→DROP COLUMN, CREATE INDEX→DROP INDEX, ADD CONSTRAINT→DROP CONSTRAINT,
/// CREATE TABLE→DROP TABLE, sp_rename→reverse sp_rename, CREATE VIEW/PROC→DROP) plus the
/// analyzer wiring of <see cref="ScriptSummary"/> rollback fields.
/// </summary>
public class RollbackBuilderTests
{
    private static SqlStatementInfo ParseSingle(string sql)
    {
        var result = new MsSqlScriptParser().Parse(sql, "test.sql", SqlServerVersion.Sql2019);
        Assert.Empty(result.Errors);
        return Assert.Single(result.Statements);
    }

    private static string? Reverse(string sql) => RollbackScriptBuilder.TryReverse(ParseSingle(sql));

    // --- REV-002: DML summarised per file, DDL per statement (ADR-0001) ---

    [Fact]
    public void Dml_without_inverse_is_one_info_finding_per_file()
    {
        const string sql = "INSERT INTO dbo.T (Id) VALUES (1);\nUPDATE dbo.T SET C = 1 WHERE Id = 1;\n"
                           + "DELETE FROM dbo.T WHERE Id = 2;\nINSERT INTO dbo.T (Id) VALUES (3);";
        var report = new MsSqlAnalyzer().Analyze([("a.sql", sql), ("b.sql", "DELETE FROM dbo.U WHERE Id = 1;")], new PlanizerConfig { Rollback = true });

        var findings = report.Findings.Where(f => f.RuleId == "MSSQL-REV-002").OrderBy(f => f.Location.File).ToList();
        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(Severity.Info, f.Severity));
        Assert.Equal(("a.sql", 1), (findings[0].Location.File, findings[0].Location.Line));
        Assert.Contains("4 data-modification statements in this file have no automatic inverse (INSERT\u00d72, DELETE\u00d71, UPDATE\u00d71)", findings[0].Message);
        Assert.Contains("1 data-modification statement in this file has no automatic inverse (DELETE\u00d71)", findings[1].Message);
        Assert.False(report.Summary.RollbackComplete);
    }

    [Fact]
    public void Suppressed_dml_statements_leave_the_per_file_count()
    {
        const string sql = "-- planizer:ignore MSSQL-REV-002 restored from staging\nINSERT INTO dbo.T (Id) VALUES (1);\n"
                           + "UPDATE dbo.T SET C = 1 WHERE Id = 1;";
        var report = new MsSqlAnalyzer().Analyze([("a.sql", sql)], new PlanizerConfig { Rollback = true });

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSSQL-REV-002");
        Assert.Equal(3, finding.Location.Line);
        Assert.Contains("1 data-modification statement", finding.Message);
    }

    [Fact]
    public void Ddl_without_inverse_is_still_reported_per_statement_as_warning()
    {
        const string sql = "DROP INDEX IX_A ON dbo.T;\nDROP INDEX IX_B ON dbo.T;";
        var report = new MsSqlAnalyzer().Analyze([("a.sql", sql)], new PlanizerConfig { Rollback = true });

        var findings = report.Findings.Where(f => f.RuleId == "MSSQL-REV-002").ToList();
        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(Severity.Warning, f.Severity));
    }

    // --- transient targets: nothing to roll back ---

    [Theory]
    [InlineData("DELETE FROM @Ids;", false)]
    [InlineData("INSERT INTO @Ids (Id) VALUES (1);", false)]
    [InlineData("UPDATE @Ids SET Id = 2;", false)]
    [InlineData("DELETE FROM #Stage;", false)]
    [InlineData("SELECT Id INTO #Stage FROM dbo.T;", false)]
    [InlineData("DELETE FROM dbo.T;", true)]
    [InlineData("INSERT INTO dbo.T (Id) VALUES (1);", true)]
    [InlineData("SELECT Id INTO dbo.T_backup FROM dbo.T;", true)]
    public void Only_persistent_dml_requires_rollback(string sql, bool expected)
        => Assert.Equal(expected, RollbackScriptBuilder.RequiresRollback(ParseSingle(sql)));

    // --- ADD COLUMN → DROP COLUMN ---

    [Fact]
    public void Add_nullable_column_reverses_to_drop_column()
        => Assert.Equal(
            "ALTER TABLE [dbo].[T] DROP COLUMN [C];",
            Reverse("ALTER TABLE dbo.T ADD C int NULL;"));

    [Fact]
    public void Add_two_columns_reverses_to_single_drop_column()
        => Assert.Equal(
            "ALTER TABLE [dbo].[T] DROP COLUMN [A], [B];",
            Reverse("ALTER TABLE dbo.T ADD A int NULL, B nvarchar(50) NULL;"));

    [Fact]
    public void Add_column_with_unnamed_default_cannot_be_reversed()
        => Assert.Null(Reverse("ALTER TABLE dbo.T ADD C int NOT NULL DEFAULT 0;"));

    [Fact]
    public void Add_column_with_named_default_drops_constraint_then_column()
        => Assert.Equal(
            "ALTER TABLE [dbo].[T] DROP CONSTRAINT [DF_T_C];\nALTER TABLE [dbo].[T] DROP COLUMN [C];",
            Reverse("ALTER TABLE dbo.T ADD C int NOT NULL CONSTRAINT DF_T_C DEFAULT 0;"));

    // --- ADD CONSTRAINT → DROP CONSTRAINT ---

    [Fact]
    public void Add_named_primary_key_reverses_to_drop_constraint()
        => Assert.Equal(
            "ALTER TABLE [dbo].[T] DROP CONSTRAINT [PK_T];",
            Reverse("ALTER TABLE dbo.T ADD CONSTRAINT PK_T PRIMARY KEY (Id);"));

    [Fact]
    public void Add_named_check_reverses_to_drop_constraint()
        => Assert.Equal(
            "ALTER TABLE [dbo].[T] DROP CONSTRAINT [CK_T_C];",
            Reverse("ALTER TABLE dbo.T ADD CONSTRAINT CK_T_C CHECK (C > 0);"));

    [Fact]
    public void Add_unnamed_check_cannot_be_reversed()
        => Assert.Null(Reverse("ALTER TABLE dbo.T ADD CHECK (C > 0);"));

    // --- CREATE INDEX → DROP INDEX ---

    [Fact]
    public void Create_index_reverses_to_drop_index()
        => Assert.Equal(
            "DROP INDEX [IX_Orders_Status] ON [dbo].[Orders];",
            Reverse("CREATE INDEX IX_Orders_Status ON dbo.Orders (Status);"));

    // --- CREATE TABLE / SELECT INTO → DROP TABLE ---

    [Fact]
    public void Create_table_reverses_to_drop_table()
        => Assert.Equal(
            "DROP TABLE [dbo].[Widgets];",
            Reverse("CREATE TABLE dbo.Widgets (Id int NOT NULL);"));

    [Fact]
    public void Select_into_reverses_to_drop_table()
        => Assert.Equal(
            "DROP TABLE [dbo].[Backup1];",
            Reverse("SELECT * INTO dbo.Backup1 FROM dbo.T;"));

    // --- CREATE VIEW / PROC / FUNCTION / TRIGGER → DROP ---

    [Fact]
    public void Create_view_reverses_to_drop_view()
        => Assert.Equal(
            "DROP VIEW [dbo].[V];",
            Reverse("CREATE VIEW dbo.V AS SELECT 1 AS C;"));

    [Fact]
    public void Create_function_reverses_to_drop_function()
        => Assert.Equal(
            "DROP FUNCTION [dbo].[F];",
            Reverse("CREATE FUNCTION dbo.F() RETURNS int AS BEGIN RETURN 1; END;"));

    [Fact]
    public void Create_trigger_reverses_to_drop_trigger()
        => Assert.Equal(
            "DROP TRIGGER [dbo].[TR_T];",
            Reverse("CREATE TRIGGER dbo.TR_T ON dbo.T AFTER INSERT AS BEGIN SET NOCOUNT ON; END;"));

    [Fact]
    public void Create_database_trigger_reverses_with_on_database()
        => Assert.Equal(
            "DROP TRIGGER [TR_DDL] ON DATABASE;",
            Reverse("CREATE TRIGGER TR_DDL ON DATABASE FOR CREATE_TABLE AS BEGIN SET NOCOUNT ON; END;"));

    // --- CREATE OR ALTER / ALTER of a module: inverse is the previous definition (source control) ---

    [Fact]
    public void Create_or_alter_view_reverses_to_redeploy_instruction()
        => Assert.StartsWith(
            "-- [dbo].[V]: redeploy the previous VIEW definition from source control",
            Reverse("CREATE OR ALTER VIEW dbo.V AS SELECT 1 AS C;"));

    [Fact]
    public void Create_or_alter_procedure_reverses_to_redeploy_instruction()
        => Assert.StartsWith(
            "-- [dbo].[usp_GetOpenOrders]: redeploy the previous PROCEDURE definition from source control",
            Reverse("CREATE OR ALTER PROCEDURE dbo.usp_GetOpenOrders AS BEGIN SET NOCOUNT ON; SELECT 1; END;"));

    [Fact]
    public void Alter_function_reverses_to_redeploy_instruction()
        => Assert.StartsWith(
            "-- [dbo].[F]: redeploy the previous FUNCTION definition from source control",
            Reverse("ALTER FUNCTION dbo.F() RETURNS int AS BEGIN RETURN 1; END;"));

    [Fact]
    public void Module_redefinition_only_script_has_complete_rollback_and_no_rev002()
    {
        var report = new MsSqlAnalyzer().Analyze(
            [("m.sql", "CREATE OR ALTER PROCEDURE dbo.usp_GetOpenOrders AS BEGIN SET NOCOUNT ON; SELECT 1; END;")],
            new PlanizerConfig { Rollback = true });

        Assert.True(report.Summary.RollbackComplete);
        Assert.Single(report.Summary.RollbackScript);
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSSQL-REV-002");
    }

    [Fact]
    public void Create_procedure_reverses_to_drop_procedure()
        => Assert.Equal(
            "DROP PROCEDURE [dbo].[P];",
            Reverse("CREATE PROCEDURE dbo.P AS SELECT 1;"));

    // --- sp_rename → reverse sp_rename ---

    [Fact]
    public void Sp_rename_table_reverses_names()
        => Assert.Equal(
            "EXEC sp_rename 'dbo.ArchivedOrders', 'OldOrders';",
            Reverse("EXEC sp_rename 'dbo.OldOrders', 'ArchivedOrders';"));

    [Fact]
    public void Sp_rename_column_reverses_with_objtype()
        => Assert.Equal(
            "EXEC sp_rename 'dbo.Orders.CustomerName', 'CustName', 'COLUMN';",
            Reverse("EXEC sp_rename 'dbo.Orders.CustName', 'CustomerName', 'COLUMN';"));

    [Fact]
    public void Sp_rename_with_variable_arguments_cannot_be_reversed()
        => Assert.Null(Reverse("EXEC sp_rename @oldName, 'NewName';"));

    [Fact]
    public void Sp_rename_with_bracketed_objname_reverses_like_the_bare_form()
        // EF Core always writes N'[Table].[Column]'.
        => Assert.Equal(
            "EXEC sp_rename 'BatchDraft.HasError', 'IsError', 'COLUMN';",
            Reverse("EXEC sp_rename N'[BatchDraft].[IsError]', N'HasError', N'COLUMN';"));

    [Fact]
    public void Sp_rename_index_reverses_with_objtype_index()
        => Assert.Equal(
            "EXEC sp_rename 'Widget.IX_Widget_New', 'IX_Widget_Old', 'INDEX';",
            Reverse("EXEC sp_rename N'[Widget].[IX_Widget_Old]', N'IX_Widget_New', 'INDEX';"));

    [Fact]
    public void Sp_rename_keeps_brackets_only_for_identifiers_that_need_them()
        => Assert.Equal(
            "EXEC sp_rename 'dbo.[Order Lines].Qty', 'Quantity', 'COLUMN';",
            Reverse("EXEC sp_rename '[dbo].[Order Lines].[Quantity]', 'Qty', 'COLUMN';"));

    [Fact]
    public void Sp_rename_with_a_dotted_new_name_cannot_be_reversed()
        => Assert.Null(Reverse("EXEC sp_rename 'dbo.T', 'dbo.T2';"));

    // --- ENABLE TRIGGER <-> DISABLE TRIGGER ---

    [Fact]
    public void Enable_trigger_reverses_to_disable_trigger()
        => Assert.Equal(
            "ALTER TABLE [dbo].[Account] DISABLE TRIGGER [cdc_CardAccount];",
            Reverse("ALTER TABLE [dbo].[Account] ENABLE TRIGGER [cdc_CardAccount];"));

    [Fact]
    public void Disable_all_triggers_reverses_to_enable_all()
        => Assert.Equal(
            "ALTER TABLE [dbo].[Account] ENABLE TRIGGER ALL;",
            Reverse("ALTER TABLE dbo.Account DISABLE TRIGGER ALL;"));

    // --- irreversible / unsupported statements produce no reverse ---

    [Theory]
    [InlineData("DROP TABLE dbo.T;")]
    [InlineData("TRUNCATE TABLE dbo.T;")]
    [InlineData("ALTER TABLE dbo.T DROP COLUMN C;")]
    [InlineData("DROP INDEX IX_T_C ON dbo.T;")]
    [InlineData("UPDATE dbo.T SET C = 1 WHERE Id = 1;")]
    [InlineData("DELETE FROM dbo.T WHERE Id = 1;")]
    [InlineData("INSERT INTO dbo.T (Id) VALUES (1);")]
    public void State_changing_statements_without_an_inverse_return_null(string sql)
        => Assert.Null(Reverse(sql));

    // --- RequiresRollback scope ---

    [Theory]
    [InlineData("SELECT 1;", false)]
    [InlineData("SELECT * INTO dbo.B FROM dbo.T;", true)]
    [InlineData("BEGIN TRAN;", false)]
    [InlineData("SET LOCK_TIMEOUT 30000;", false)]
    [InlineData("CREATE TABLE dbo.T (Id int NOT NULL);", true)]
    [InlineData("UPDATE dbo.T SET C = 1;", true)]
    [InlineData("EXEC (@sql);", true)]
    // Index maintenance that restores the identical schema needs no rollback entry…
    [InlineData("ALTER INDEX IX_T_C ON dbo.T REBUILD;", false)]
    [InlineData("ALTER INDEX ALL ON dbo.T REORGANIZE;", false)]
    // …but a REBUILD WITH (…) changes persisted index settings, and DISABLE changes state.
    [InlineData("ALTER INDEX IX_T_C ON dbo.T REBUILD WITH (FILLFACTOR = 80);", true)]
    [InlineData("ALTER INDEX IX_T_C ON dbo.T DISABLE;", true)]
    public void RequiresRollback_covers_state_changing_statements_only(string sql, bool expected)
        => Assert.Equal(expected, RollbackScriptBuilder.RequiresRollback(ParseSingle(sql)));

    // --- analyzer wiring: ScriptSummary rollback fields ---

    [Fact]
    public void Summary_collects_rollback_statements_in_reverse_order_and_marks_complete()
    {
        const string sql = """
            CREATE TABLE dbo.Widgets (Id int NOT NULL);
            CREATE INDEX IX_Widgets_Id ON dbo.Widgets (Id);
            """;

        var report = new MsSqlAnalyzer().Analyze([("m.sql", sql)], new PlanizerConfig { Rollback = true });

        Assert.True(report.Summary.RollbackComplete);
        Assert.Equal(
            ["DROP INDEX [IX_Widgets_Id] ON [dbo].[Widgets];", "DROP TABLE [dbo].[Widgets];"],
            report.Summary.RollbackScript);
    }

    [Fact]
    public void Summary_marks_rollback_incomplete_when_a_statement_has_no_inverse()
    {
        const string sql = """
            CREATE INDEX IX_Orders_Status ON dbo.Orders (Status);
            UPDATE dbo.Orders SET Status = 1 WHERE Status = 0;
            """;

        var report = new MsSqlAnalyzer().Analyze([("m.sql", sql)], new PlanizerConfig { Rollback = true });

        Assert.False(report.Summary.RollbackComplete);
        Assert.Equal(["DROP INDEX [IX_Orders_Status] ON [dbo].[Orders];"], report.Summary.RollbackScript);
    }

    [Fact]
    public void Truncate_message_reflects_the_transaction_rollback_window()
    {
        // The fixture harness cannot assert message content; the in-transaction nuance of
        // MSSQL-REV-004 is the message, so it is pinned here.
        var analyzer = new MsSqlAnalyzer();
        var config = new PlanizerConfig { Rollback = true };

        var bare = analyzer.Analyze([("a.sql", "TRUNCATE TABLE dbo.T;")], config);
        var inTran = analyzer.Analyze([("b.sql", "BEGIN TRAN;\nTRUNCATE TABLE dbo.T;\nCOMMIT;")], config);

        var bareFinding = Assert.Single(bare.Findings, f => f.RuleId == "MSSQL-REV-004");
        var tranFinding = Assert.Single(inTran.Findings, f => f.RuleId == "MSSQL-REV-004");

        Assert.Contains("no rollback window", bareFinding.Message);
        Assert.Contains("can be rolled back until the enclosing transaction commits", tranFinding.Message);
        Assert.True(bareFinding.Inconclusive);
        Assert.True(tranFinding.Inconclusive);
    }

    [Fact]
    public void Summary_counts_irreversible_and_unanalyzable_statements()
    {
        const string sql = """
            DROP TABLE dbo.Legacy;
            TRUNCATE TABLE dbo.AuditLog;
            EXEC (@sql);
            SELECT 1;
            """;

        var report = new MsSqlAnalyzer().Analyze([("m.sql", sql)], new PlanizerConfig { Rollback = true });

        Assert.Equal(2, report.Summary.IrreversibleCount);
        Assert.Equal(1, report.Summary.UnanalyzableCount);
        Assert.False(report.Summary.RollbackComplete);
    }
}
