using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

public class ClassifierTests
{
    [Theory]
    // DDL: Create / Alter / Drop / Truncate / Rename family
    [InlineData("CREATE TABLE dbo.T (Id int);", StatementKind.Ddl)]
    [InlineData("ALTER TABLE dbo.T ADD C int NULL;", StatementKind.Ddl)]
    [InlineData("ALTER TABLE dbo.T SWITCH TO dbo.T2;", StatementKind.Ddl)]
    [InlineData("DROP TABLE dbo.T;", StatementKind.Ddl)]
    [InlineData("TRUNCATE TABLE dbo.T;", StatementKind.Ddl)]
    [InlineData("CREATE INDEX IX_T ON dbo.T (Id);", StatementKind.Ddl)]
    [InlineData("ALTER INDEX IX_T ON dbo.T REBUILD;", StatementKind.Ddl)]
    [InlineData("DROP INDEX IX_T ON dbo.T;", StatementKind.Ddl)]
    [InlineData("CREATE VIEW dbo.V AS SELECT 1 AS One;", StatementKind.Ddl)]
    [InlineData("EXEC sp_rename 'dbo.T', 'T2';", StatementKind.Ddl)]
    // DML
    [InlineData("INSERT INTO dbo.T (Id) VALUES (1);", StatementKind.Dml)]
    [InlineData("UPDATE dbo.T SET Id = 2;", StatementKind.Dml)]
    [InlineData("DELETE FROM dbo.T;", StatementKind.Dml)]
    [InlineData("SELECT * FROM dbo.T;", StatementKind.Dml)]
    [InlineData(
        "MERGE dbo.T AS t USING dbo.S AS s ON t.Id = s.Id WHEN MATCHED THEN UPDATE SET t.Id = s.Id;",
        StatementKind.Dml)]
    // DCL
    [InlineData("GRANT SELECT ON dbo.T TO SomeUser;", StatementKind.Dcl)]
    [InlineData("DENY SELECT ON dbo.T TO SomeUser;", StatementKind.Dcl)]
    [InlineData("REVOKE SELECT ON dbo.T FROM SomeUser;", StatementKind.Dcl)]
    // Flow: If / While / BeginEnd / Try / Tran / Set
    [InlineData("IF 1 = 1 SELECT 1;", StatementKind.Flow)]
    [InlineData("WHILE 1 = 0 BREAK;", StatementKind.Flow)]
    [InlineData("BEGIN SELECT 1; END", StatementKind.Flow)]
    [InlineData("BEGIN TRY SELECT 1; END TRY BEGIN CATCH SELECT 2; END CATCH", StatementKind.Flow)]
    [InlineData("BEGIN TRANSACTION;", StatementKind.Flow)]
    [InlineData("COMMIT;", StatementKind.Flow)]
    [InlineData("ROLLBACK;", StatementKind.Flow)]
    [InlineData("SET LOCK_TIMEOUT 30000;", StatementKind.Flow)]
    [InlineData("SET IDENTITY_INSERT dbo.T ON;", StatementKind.Flow)]
    // Dynamic SQL
    [InlineData("EXEC('DROP TABLE dbo.T');", StatementKind.Dynamic)]
    [InlineData("EXEC sp_executesql N'SELECT 1';", StatementKind.Dynamic)]
    // Plain procedure call: not analyzable as DDL/DML, but not dynamic either
    [InlineData("EXEC dbo.MyProc;", StatementKind.Other)]
    public void Classifies_single_statement(string sql, StatementKind expected)
    {
        // Control-flow wrappers (IF / WHILE / BEGIN-END / TRY) are flattened together with their
        // bodies, so the wrapper under test is the first statement, not necessarily the only one.
        var statement = Parse(sql)[0];

        Assert.Equal(expected, statement.Kind);
    }

    [Fact]
    public void Exec_of_a_variable_is_dynamic()
    {
        var statements = Parse("DECLARE @s nvarchar(200);\nEXEC(@s);");

        Assert.Equal(StatementKind.Dynamic, statements[1].Kind);
    }

    [Fact]
    public void Exec_of_a_procedure_name_variable_is_dynamic()
    {
        var statements = Parse("DECLARE @p sysname;\nEXEC @p;");

        Assert.Equal(StatementKind.Dynamic, statements[1].Kind);
    }

    [Fact]
    public void Statement_info_carries_location_sql_and_index()
    {
        var statements = Parse("SELECT 1;\nDROP TABLE dbo.T;");

        Assert.Equal(2, statements.Count);
        Assert.Equal(0, statements[0].Index);
        Assert.Equal(1, statements[1].Index);
        Assert.Equal(new SourceLocation("test.sql", 2, 1), statements[1].Location);
        Assert.Equal("DROP TABLE dbo.T", statements[1].Sql.TrimEnd(';'));
    }

    [Fact]
    public void Statements_across_go_batches_are_flattened_in_order()
    {
        var statements = Parse("SELECT 1;\nGO\nSELECT 2;\nGO\nSELECT 3;");

        Assert.Equal(3, statements.Count);
        Assert.Equal(new[] { 0, 1, 2 }, statements.Select(s => s.Index));
    }

    private static IReadOnlyList<SqlStatementInfo> Parse(string sql)
    {
        var result = new MsSqlScriptParser().Parse(sql, "test.sql", SqlServerVersion.Sql2019);
        Assert.Empty(result.Errors);
        return result.Statements;
    }
}
