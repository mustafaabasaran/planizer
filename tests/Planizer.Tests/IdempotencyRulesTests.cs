using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

/// <summary>
/// Message and fix wording of the MSSQL-IDEM family; fixtures cover rule/severity/line, the
/// version-dependent fix text (CREATE OR ALTER, DROP … IF EXISTS) is asserted here.
/// </summary>
public class IdempotencyRulesTests
{
    private static Finding Single(string ruleId, string sql, SqlServerVersion version = SqlServerVersion.Sql2019)
    {
        var report = new MsSqlAnalyzer().Analyze(
            [("m.sql", sql)],
            new PlanizerConfig { TargetVersion = version });

        return Assert.Single(report.Findings, f => f.RuleId == ruleId);
    }

    [Fact]
    public void Create_table_fix_wraps_the_statement_in_an_object_id_guard()
    {
        var finding = Single("MSSQL-IDEM-001", "CREATE TABLE dbo.Orders (Id int NOT NULL);");

        Assert.Contains("CREATE TABLE dbo.Orders is not guarded", finding.Message);
        Assert.Contains("(error 2714)", finding.Message);
        Assert.Contains("IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL BEGIN CREATE TABLE dbo.Orders", finding.Fix);
    }

    [Fact]
    public void Create_index_fix_checks_sys_indexes_for_the_table()
    {
        var finding = Single("MSSQL-IDEM-001", "CREATE INDEX IX_Orders_Total ON dbo.Orders (Total);");

        Assert.Contains("(error 1913)", finding.Message);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_Total' AND object_id = OBJECT_ID(N'dbo.Orders'))", finding.Fix);
    }

    [Fact]
    public void Create_schema_fix_uses_exec_because_create_schema_must_be_alone_in_its_batch()
    {
        var finding = Single("MSSQL-IDEM-001", "CREATE SCHEMA audit;");

        Assert.Equal("Guard it: IF SCHEMA_ID(N'audit') IS NULL EXEC(N'CREATE SCHEMA audit');", finding.Fix);
    }

    [Theory]
    [InlineData(SqlServerVersion.Sql2017)]
    [InlineData(SqlServerVersion.Sql2022)]
    [InlineData(SqlServerVersion.AzureSql)]
    public void Module_fix_suggests_create_or_alter_from_2017_on(SqlServerVersion version)
    {
        var finding = Single("MSSQL-IDEM-001", "CREATE PROCEDURE dbo.GetOrders AS SELECT 1;", version);

        Assert.Equal("Use CREATE OR ALTER PROCEDURE dbo.GetOrders so the script can be re-run.", finding.Fix);
    }

    [Fact]
    public void Module_fix_on_2016_mentions_the_sp1_requirement()
    {
        var finding = Single("MSSQL-IDEM-001", "CREATE VIEW dbo.V AS SELECT 1 AS X;", SqlServerVersion.Sql2016);

        Assert.StartsWith("Use CREATE OR ALTER VIEW dbo.V (requires SQL Server 2016 SP1)", finding.Fix);
        Assert.Contains("IF OBJECT_ID(N'dbo.V', N'V') IS NOT NULL DROP VIEW dbo.V; GO", finding.Fix);
    }

    [Fact]
    public void Module_fix_before_2016_is_a_guarded_drop_in_its_own_batch()
    {
        var finding = Single("MSSQL-IDEM-001", "CREATE PROCEDURE dbo.GetOrders AS SELECT 1;", SqlServerVersion.Sql2014);

        Assert.Equal(
            "Drop it first in its own batch: IF OBJECT_ID(N'dbo.GetOrders', N'P') IS NOT NULL DROP PROCEDURE dbo.GetOrders; GO",
            finding.Fix);
    }

    [Fact]
    public void Add_column_fix_is_the_statement_behind_a_col_length_guard()
    {
        var finding = Single("MSSQL-IDEM-002", "ALTER TABLE dbo.Orders\n    ADD Status tinyint NULL;");

        Assert.Contains("ALTER TABLE dbo.Orders ADD column Status is not guarded", finding.Message);
        Assert.Contains("(error 2705)", finding.Message);
        Assert.Equal(
            "Guard it: IF COL_LENGTH(N'dbo.Orders', N'Status') IS NULL ALTER TABLE dbo.Orders ADD Status tinyint NULL;",
            finding.Fix);
    }

    [Fact]
    public void Add_constraint_fix_checks_sys_objects_under_the_parent_table()
    {
        var finding = Single("MSSQL-IDEM-002", "ALTER TABLE dbo.Orders ADD CONSTRAINT CK_Total CHECK (Total >= 0);");

        Assert.Contains("constraint CK_Total", finding.Message);
        Assert.Contains("a constraint named CK_Total already exists (error 2714)", finding.Message);
        Assert.StartsWith(
            "Guard it: IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = N'CK_Total' AND parent_object_id = OBJECT_ID(N'dbo.Orders'))",
            finding.Fix);
    }

    [Fact]
    public void Unnamed_primary_key_fails_on_rerun_and_is_guarded_by_tablehasprimarykey()
    {
        var finding = Single("MSSQL-IDEM-002", "ALTER TABLE dbo.Orders ADD PRIMARY KEY (Id);");

        Assert.Contains("constraint (unnamed)", finding.Message);
        Assert.Contains("running the script a second time fails because the table already has a primary key (error 1779)", finding.Message);
        Assert.Equal(
            "Name the constraint and guard it: IF OBJECTPROPERTY(OBJECT_ID(N'dbo.Orders'), 'TableHasPrimaryKey') = 0 ALTER TABLE dbo.Orders ADD PRIMARY KEY (Id);",
            finding.Fix);
    }

    [Fact]
    public void Unnamed_default_fails_on_rerun_and_is_guarded_through_sys_default_constraints()
    {
        var finding = Single("MSSQL-IDEM-002", "ALTER TABLE dbo.Orders ADD DEFAULT 0 FOR Total;");

        Assert.Contains("the column already has a DEFAULT constraint (error 1781)", finding.Message);
        Assert.StartsWith(
            "Name the constraint and guard it: IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.Orders') " +
            "AND parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Orders'), N'Total', 'ColumnId'))",
            finding.Fix);
    }

    [Theory]
    [InlineData("ALTER TABLE dbo.Orders ADD CHECK (Total >= 0);", "CHECK")]
    [InlineData("ALTER TABLE dbo.Orders ADD FOREIGN KEY (CustomerId) REFERENCES dbo.Customers (Id);", "FOREIGN KEY")]
    [InlineData("ALTER TABLE dbo.Orders ADD UNIQUE (Number);", "UNIQUE")]
    public void Unnamed_check_fk_and_unique_do_not_fail_but_duplicate_themselves(string sql, string kind)
    {
        var finding = Single("MSSQL-IDEM-002", sql);

        Assert.Contains("constraint (unnamed)", finding.Message);
        Assert.DoesNotContain("fails", finding.Message);
        Assert.EndsWith($"running the script a second time does not fail but adds a second, duplicate {kind} constraint under a new system-generated name.", finding.Message);
        Assert.StartsWith("Name the constraint and guard it: IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name = N'<constraint>'", finding.Fix);
    }

    [Fact]
    public void Drop_column_fix_uses_if_exists_from_2016_and_col_length_before()
    {
        var modern = Single("MSSQL-IDEM-002", "ALTER TABLE dbo.Orders DROP COLUMN Legacy;", SqlServerVersion.Sql2016);
        Assert.Equal("Use ALTER TABLE dbo.Orders DROP COLUMN IF EXISTS Legacy;", modern.Fix);
        Assert.Contains("(error 4924)", modern.Message);

        var legacy = Single("MSSQL-IDEM-002", "ALTER TABLE dbo.Orders DROP COLUMN Legacy;", SqlServerVersion.Sql2014);
        Assert.Equal(
            "Guard it: IF COL_LENGTH(N'dbo.Orders', N'Legacy') IS NOT NULL ALTER TABLE dbo.Orders DROP COLUMN Legacy;",
            legacy.Fix);
    }

    [Fact]
    public void Drop_constraint_fix_uses_if_exists_from_2016_on()
    {
        var finding = Single("MSSQL-IDEM-002", "ALTER TABLE dbo.Orders DROP CONSTRAINT FK_Orders_Customers;");

        Assert.Contains("(error 3728)", finding.Message);
        Assert.Equal("Use ALTER TABLE dbo.Orders DROP CONSTRAINT IF EXISTS FK_Orders_Customers;", finding.Fix);
    }

    [Fact]
    public void Mixed_add_lists_every_unguarded_element_once()
    {
        var finding = Single("MSSQL-IDEM-002", "ALTER TABLE dbo.Orders ADD Status tinyint NULL, CONSTRAINT CK_S CHECK (Status < 5);");

        Assert.Contains("ADD column Status, constraint CK_S is not guarded", finding.Message);
    }

    [Fact]
    public void Drop_table_fix_is_drop_if_exists_from_2016_on()
    {
        var finding = Single("MSSQL-IDEM-003", "DROP TABLE dbo.Legacy;", SqlServerVersion.Sql2016);

        Assert.Contains("DROP TABLE dbo.Legacy is not guarded", finding.Message);
        Assert.Contains("(error 3701)", finding.Message);
        Assert.Equal("Use DROP TABLE IF EXISTS dbo.Legacy;", finding.Fix);
    }

    [Fact]
    public void Drop_table_fix_before_2016_is_an_object_id_guard()
    {
        var finding = Single("MSSQL-IDEM-003", "DROP TABLE dbo.Legacy;", SqlServerVersion.Sql2014);

        Assert.Equal(
            "Guard it (DROP … IF EXISTS needs SQL Server 2016): IF OBJECT_ID(N'dbo.Legacy', N'U') IS NOT NULL DROP TABLE dbo.Legacy;",
            finding.Fix);
    }

    [Fact]
    public void Drop_index_fix_keeps_the_on_clause()
    {
        var modern = Single("MSSQL-IDEM-003", "DROP INDEX IX_Orders_Total ON dbo.Orders;");
        Assert.Equal("Use DROP INDEX IF EXISTS IX_Orders_Total ON dbo.Orders;", modern.Fix);

        var legacy = Single("MSSQL-IDEM-003", "DROP INDEX IX_Orders_Total ON dbo.Orders;", SqlServerVersion.Sql2014);
        Assert.Contains(
            "IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_Total' AND object_id = OBJECT_ID(N'dbo.Orders')) DROP INDEX IX_Orders_Total ON dbo.Orders;",
            legacy.Fix);
    }

    [Fact]
    public void Legacy_drop_index_spelling_resolves_table_and_index()
    {
        var finding = Single("MSSQL-IDEM-003", "DROP INDEX dbo.Orders.IX_Orders_Total;");

        Assert.Contains("DROP INDEX IX_Orders_Total is not guarded", finding.Message);
        Assert.Equal("Use DROP INDEX IF EXISTS IX_Orders_Total ON dbo.Orders;", finding.Fix);
    }

    [Fact]
    public void Multi_object_drop_is_one_finding_listing_every_object()
    {
        var finding = Single("MSSQL-IDEM-003", "DROP TABLE dbo.A, dbo.B;");

        Assert.Contains("DROP TABLE dbo.A, dbo.B is not guarded", finding.Message);
        Assert.Contains("the tables are already gone", finding.Message);
        Assert.Equal("Use DROP TABLE IF EXISTS dbo.A, dbo.B;", finding.Fix);
    }

    [Fact]
    public void Drop_schema_cites_its_own_error_number_and_schema_id_guard()
    {
        var finding = Single("MSSQL-IDEM-003", "DROP SCHEMA audit;", SqlServerVersion.Sql2014);

        Assert.Contains("(error 15151)", finding.Message);
        Assert.Contains("IF SCHEMA_ID(N'audit') IS NOT NULL DROP SCHEMA audit;", finding.Fix);
    }

    [Fact]
    public void Database_trigger_drop_fix_keeps_the_scope()
    {
        var finding = Single("MSSQL-IDEM-003", "DROP TRIGGER trg_ddl ON DATABASE;");

        Assert.Equal("Use DROP TRIGGER IF EXISTS trg_ddl ON DATABASE;", finding.Fix);
    }
}
