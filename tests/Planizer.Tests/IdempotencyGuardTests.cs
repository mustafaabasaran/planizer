using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

public class IdempotencyGuardTests
{
    [Theory]
    [InlineData("IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.T') AND name = 'C')\n    ALTER TABLE dbo.T ADD C int NULL;")]
    [InlineData("IF OBJECT_ID('dbo.T') IS NULL\n    CREATE TABLE dbo.T (Id int NOT NULL);")]
    [InlineData("IF OBJECT_ID(N'dbo.T', N'U') IS NOT NULL\n    DROP TABLE dbo.T;")]
    [InlineData("IF COL_LENGTH('dbo.T', 'C') IS NULL\n    ALTER TABLE dbo.T ADD C int NULL;")]
    [InlineData("IF INDEXPROPERTY(OBJECT_ID('dbo.T'), 'IX_T', 'IndexID') IS NULL\n    CREATE INDEX IX_T ON dbo.T (Id);")]
    [InlineData("IF COLUMNPROPERTY(OBJECT_ID('dbo.T'), 'C', 'ColumnId') IS NULL\n    ALTER TABLE dbo.T ADD C int NULL;")]
    [InlineData("IF TYPE_ID('dbo.MyType') IS NULL\n    CREATE TYPE dbo.MyType FROM int;")]
    [InlineData("IF SCHEMA_ID('audit') IS NULL\n    EXEC('CREATE SCHEMA audit');")]
    [InlineData("IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'T')\n    CREATE TABLE dbo.T (Id int NOT NULL);")]
    [InlineData("IF NOT EXISTS (SELECT * FROM sysobjects WHERE name = 'T')\n    CREATE TABLE dbo.T (Id int NOT NULL);")]
    [InlineData("IF (SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_T') = 0\n    CREATE INDEX IX_T ON dbo.T (Id);")]
    public void Catalog_predicates_guard_the_then_branch(string sql)
    {
        var statement = LastStatement(sql);

        Assert.True(IdempotencyGuard.IsGuarded(statement));
    }

    [Fact]
    public void Else_branch_of_an_exists_check_is_guarded()
    {
        var statements = Parse("""
            IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'T' AND schema_id = SCHEMA_ID('dbo'))
                PRINT 'exists';
            ELSE
                CREATE TABLE dbo.T (Id int NOT NULL);
            """);

        var create = Assert.Single(statements, s => s.Ast is CreateTableStatement);
        Assert.True(create.InElseBranch);
        Assert.True(IdempotencyGuard.IsGuarded(create));
    }

    [Fact]
    public void Guard_is_found_through_begin_end_and_try_blocks()
    {
        var statements = Parse("""
            IF OBJECT_ID('dbo.T') IS NULL
            BEGIN
                BEGIN TRY
                    CREATE TABLE dbo.T (Id int NOT NULL);
                END TRY
                BEGIN CATCH
                    THROW;
                END CATCH
            END
            """);

        var create = Assert.Single(statements, s => s.Ast is CreateTableStatement);
        Assert.True(IdempotencyGuard.IsGuarded(create));
    }

    [Fact]
    public void Outer_if_guards_statements_under_an_unrelated_inner_if()
    {
        var statements = Parse("""
            IF OBJECT_ID('dbo.T') IS NULL
            BEGIN
                IF @@SERVERNAME = 'PROD'
                    CREATE TABLE dbo.T (Id int NOT NULL);
            END
            """);

        var create = Assert.Single(statements, s => s.Ast is CreateTableStatement);
        Assert.True(IdempotencyGuard.IsGuarded(create));
    }

    [Theory]
    [InlineData("DECLARE @flag bit = 1;\nIF @flag = 1\n    CREATE TABLE dbo.T (Id int NOT NULL);")]
    [InlineData("IF @@SERVERNAME = 'PROD'\n    CREATE TABLE dbo.T (Id int NOT NULL);")]
    [InlineData("IF (SELECT COUNT(*) FROM dbo.Settings WHERE Name = 'x') = 0\n    CREATE TABLE dbo.T (Id int NOT NULL);")]
    [InlineData("CREATE TABLE dbo.T (Id int NOT NULL);")]
    [InlineData("BEGIN\n    CREATE TABLE dbo.T (Id int NOT NULL);\nEND")]
    [InlineData("WHILE 1 = 0\n    CREATE TABLE dbo.T (Id int NOT NULL);")]
    public void Non_catalog_predicates_and_bare_statements_are_not_guarded(string sql)
    {
        var statement = LastStatement(sql);

        Assert.False(IdempotencyGuard.IsGuarded(statement));
    }

    [Fact]
    public void Drop_if_exists_earlier_in_the_file_counts_as_dropped()
    {
        var (context, statements) = Analyze("""
            DROP TABLE IF EXISTS dbo.X;
            CREATE TABLE dbo.X (Id int NOT NULL);
            """);

        var create = (CreateTableStatement)statements[1].Ast;
        Assert.True(IdempotencyGuard.IsDroppedEarlierInFile(statements[1], context, create.SchemaObjectName));
    }

    [Fact]
    public void Guarded_drop_earlier_in_the_file_counts_as_dropped()
    {
        var (context, statements) = Analyze("""
            IF OBJECT_ID('dbo.X') IS NOT NULL
                DROP TABLE [dbo].[X];
            GO
            CREATE TABLE dbo.X (Id int NOT NULL);
            """);

        var create = Assert.Single(statements, s => s.Ast is CreateTableStatement);
        var name = ((CreateTableStatement)create.Ast).SchemaObjectName;
        Assert.True(IdempotencyGuard.IsDroppedEarlierInFile(create, context, name));
    }

    [Fact]
    public void Unguarded_drop_does_not_count()
    {
        var (context, statements) = Analyze("""
            DROP TABLE dbo.X;
            CREATE TABLE dbo.X (Id int NOT NULL);
            """);

        var create = (CreateTableStatement)statements[1].Ast;
        Assert.False(IdempotencyGuard.IsDroppedEarlierInFile(statements[1], context, create.SchemaObjectName));
    }

    [Fact]
    public void Drop_of_a_different_object_or_in_another_file_does_not_count()
    {
        var (context, statements) = Analyze(
            ("one.sql", "DROP TABLE IF EXISTS dbo.X;"),
            ("two.sql", "DROP TABLE IF EXISTS dbo.Y;\nCREATE TABLE dbo.X (Id int NOT NULL);"));

        var create = Assert.Single(statements, s => s.Ast is CreateTableStatement);
        var name = ((CreateTableStatement)create.Ast).SchemaObjectName;
        Assert.False(IdempotencyGuard.IsDroppedEarlierInFile(create, context, name));
    }

    [Fact]
    public void Drop_after_the_create_does_not_count()
    {
        var (context, statements) = Analyze("""
            CREATE TABLE dbo.X (Id int NOT NULL);
            DROP TABLE IF EXISTS dbo.X;
            """);

        var create = (CreateTableStatement)statements[0].Ast;
        Assert.False(IdempotencyGuard.IsDroppedEarlierInFile(statements[0], context, create.SchemaObjectName));
    }

    [Fact]
    public void Drop_index_if_exists_matches_an_index_by_name()
    {
        var (context, statements) = Analyze("""
            DROP INDEX IF EXISTS IX_T_C ON dbo.T;
            CREATE INDEX IX_T_C ON dbo.T (C);
            """);

        var create = (CreateIndexStatement)statements[1].Ast;
        var name = new SchemaObjectName();
        name.Identifiers.Add(create.Name);
        Assert.True(IdempotencyGuard.IsDroppedEarlierInFile(statements[1], context, name));
    }

    [Theory]
    [InlineData("IF OBJECT_ID('dbo.T') IS NOT NULL RETURN;\nCREATE TABLE dbo.T (Id int NOT NULL);")]
    [InlineData("IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.T') AND name = 'C') RETURN;\nALTER TABLE dbo.T ADD C int NULL;")]
    [InlineData("IF OBJECT_ID('dbo.T') IS NULL RETURN;\nDROP TABLE dbo.T;")]
    [InlineData("IF OBJECT_ID('dbo.T') IS NOT NULL BEGIN PRINT 'exists'; RETURN; END\nCREATE TABLE dbo.T (Id int NOT NULL);")]
    [InlineData("IF OBJECT_ID('dbo.T') IS NOT NULL THROW 50000, 'already applied', 1;\nCREATE TABLE dbo.T (Id int NOT NULL);")]
    [InlineData("IF OBJECT_ID('dbo.T') IS NOT NULL GOTO Done;\nCREATE TABLE dbo.T (Id int NOT NULL);\nDone:")]
    [InlineData("IF OBJECT_ID('dbo.T') IS NOT NULL RETURN;\nBEGIN TRY\n    CREATE TABLE dbo.T (Id int NOT NULL);\nEND TRY\nBEGIN CATCH\n    THROW;\nEND CATCH")]
    [InlineData("IF OBJECT_ID('dbo.T') IS NOT NULL RETURN;\nSELECT 1;\nIF @@SERVERNAME = 'PROD'\n    CREATE TABLE dbo.T (Id int NOT NULL);")]
    public void Exit_guard_earlier_in_the_batch_guards_what_follows(string sql)
    {
        var (context, statements) = Analyze(sql);
        var target = statements.Last(s => s.Ast is CreateTableStatement or AlterTableStatement or DropTableStatement);

        Assert.False(IdempotencyGuard.IsGuarded(target));
        Assert.True(IdempotencyGuard.IsGuarded(target, context));
    }

    [Theory]
    [InlineData("IF OBJECT_ID('dbo.T') IS NOT NULL RETURN;\nGO\nCREATE TABLE dbo.T (Id int NOT NULL);")]
    [InlineData("CREATE TABLE dbo.T (Id int NOT NULL);\nIF OBJECT_ID('dbo.T') IS NOT NULL RETURN;")]
    [InlineData("IF @@SERVERNAME = 'PROD' RETURN;\nCREATE TABLE dbo.T (Id int NOT NULL);")]
    [InlineData("IF OBJECT_ID('dbo.T') IS NOT NULL PRINT 'exists';\nCREATE TABLE dbo.T (Id int NOT NULL);")]
    [InlineData("IF OBJECT_ID('dbo.T') IS NOT NULL RAISERROR('exists', 16, 1);\nCREATE TABLE dbo.T (Id int NOT NULL);")]
    [InlineData("IF @@SERVERNAME = 'PROD'\nBEGIN\n    IF OBJECT_ID('dbo.T') IS NOT NULL RETURN;\nEND\nCREATE TABLE dbo.T (Id int NOT NULL);")]
    [InlineData("IF OBJECT_ID('dbo.T') IS NOT NULL PRINT 'x'; ELSE RETURN;\nCREATE TABLE dbo.T (Id int NOT NULL);")]
    public void Exit_guard_must_precede_the_statement_in_the_same_batch_and_scope(string sql)
    {
        var (context, statements) = Analyze(sql);
        var create = Assert.Single(statements, s => s.Ast is CreateTableStatement);

        Assert.False(IdempotencyGuard.IsGuarded(create, context));
    }

    private static SqlStatementInfo LastStatement(string sql) => Parse(sql)[^1];

    private static IReadOnlyList<SqlStatementInfo> Parse(string sql)
    {
        var result = new MsSqlScriptParser().Parse(sql, "test.sql", SqlServerVersion.Sql2019);
        Assert.Empty(result.Errors);
        return result.Statements;
    }

    private static (MsSqlAnalysisContext Context, IReadOnlyList<SqlStatementInfo> Statements) Analyze(string sql)
        => Analyze(("test.sql", sql));

    private static (MsSqlAnalysisContext Context, IReadOnlyList<SqlStatementInfo> Statements) Analyze(
        params (string Path, string Sql)[] files)
    {
        MsSqlAnalysisContext? seen = null;
        var report = new MsSqlAnalyzer(rules: [new ContextProbeRule(c => seen = c)])
            .Analyze(files, new PlanizerConfig());

        Assert.DoesNotContain(report.Findings, f => f.RuleId == MsSqlAnalyzer.ParseRuleId);
        Assert.NotNull(seen);
        return (seen, seen.Statements);
    }

    private sealed class ContextProbeRule(Action<MsSqlAnalysisContext> onAnalyze) : MsSqlRuleBase
    {
        public override string Id => "TEST-PROBE-003";
        public override string Title => "Test rule: captures the analysis context";
        public override Severity DefaultSeverity => Severity.Info;

        protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
        {
            onAnalyze(context);
            yield break;
        }
    }
}
