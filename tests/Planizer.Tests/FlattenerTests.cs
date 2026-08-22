using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

/// <summary>
/// Statements nested in IF / BEGIN-END / WHILE / TRY-CATCH run at deploy time and must reach the
/// rules; module bodies (procedures, functions, triggers, views) are definitions and must not.
/// </summary>
public class FlattenerTests
{
    [Fact]
    public void Alter_table_inside_if_is_listed_with_its_enclosing_if()
    {
        var statements = Parse("""
            IF COL_LENGTH('dbo.T', 'C') IS NULL
                ALTER TABLE dbo.T ADD C int NULL;
            """);

        Assert.Equal(2, statements.Count);
        var ifInfo = statements[0];
        var alter = statements[1];

        Assert.IsType<IfStatement>(ifInfo.Ast);
        Assert.Equal(StatementKind.Flow, ifInfo.Kind);
        Assert.Equal(0, ifInfo.Depth);
        Assert.Null(ifInfo.Parent);
        Assert.Null(ifInfo.EnclosingIf);

        Assert.IsType<AlterTableAddTableElementStatement>(alter.Ast);
        Assert.Equal(StatementKind.Ddl, alter.Kind);
        Assert.Equal(1, alter.Depth);
        Assert.Same(ifInfo, alter.Parent);
        Assert.Same(ifInfo.Ast, alter.EnclosingIf);
        Assert.False(alter.InElseBranch);
        Assert.Equal(new SourceLocation("test.sql", 2, 5), alter.Location);
    }

    [Fact]
    public void Begin_end_is_skipped_when_resolving_the_enclosing_if()
    {
        var statements = Parse("""
            IF OBJECT_ID('dbo.T') IS NULL
            BEGIN
                CREATE TABLE dbo.T (Id int NOT NULL);
                CREATE INDEX IX_T ON dbo.T (Id);
            END
            """);

        Assert.Equal(4, statements.Count);
        var ifInfo = statements[0];
        var block = statements[1];
        var create = statements[2];
        var index = statements[3];

        Assert.IsType<BeginEndBlockStatement>(block.Ast);
        Assert.Equal(1, block.Depth);
        Assert.Same(ifInfo, block.Parent);
        Assert.Same(ifInfo.Ast, block.EnclosingIf);

        Assert.IsType<CreateTableStatement>(create.Ast);
        Assert.Equal(2, create.Depth);
        Assert.Same(block, create.Parent);
        Assert.Same(ifInfo, create.Parent!.Parent);
        Assert.Same(ifInfo.Ast, create.EnclosingIf);

        Assert.IsType<CreateIndexStatement>(index.Ast);
        Assert.Same(ifInfo.Ast, index.EnclosingIf);
        Assert.Equal(2, index.Depth);
    }

    [Fact]
    public void Else_branch_is_flagged()
    {
        var statements = Parse("""
            IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'T')
                PRINT 'exists';
            ELSE
                CREATE TABLE dbo.T (Id int NOT NULL);
            """);

        Assert.Equal(3, statements.Count);
        var print = statements[1];
        var create = statements[2];

        Assert.IsType<PrintStatement>(print.Ast);
        Assert.False(print.InElseBranch);
        Assert.Same(statements[0].Ast, print.EnclosingIf);

        Assert.IsType<CreateTableStatement>(create.Ast);
        Assert.True(create.InElseBranch);
        Assert.Same(statements[0].Ast, create.EnclosingIf);
        Assert.Equal(1, create.Depth);
    }

    [Fact]
    public void Nested_if_resolves_to_the_innermost_if()
    {
        var statements = Parse("""
            IF 1 = 1
            BEGIN
                IF 2 = 2
                    DROP TABLE dbo.T;
            END
            """);

        var drop = Assert.Single(statements, s => s.Ast is DropTableStatement);
        var innerIf = statements[2];

        Assert.IsType<IfStatement>(innerIf.Ast);
        Assert.Same(innerIf.Ast, drop.EnclosingIf);
        Assert.Equal(3, drop.Depth);
        Assert.Same(innerIf, drop.Parent);
    }

    [Fact]
    public void Try_and_catch_bodies_carry_their_flags()
    {
        var statements = Parse("""
            BEGIN TRY
                BEGIN TRAN;
                ALTER TABLE dbo.T ADD C int NULL;
                COMMIT;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT > 0 ROLLBACK;
                THROW;
            END CATCH
            """);

        var tryCatch = statements[0];
        Assert.IsType<TryCatchStatement>(tryCatch.Ast);
        Assert.False(tryCatch.InTryBlock);
        Assert.False(tryCatch.InCatchBlock);

        var alter = Assert.Single(statements, s => s.Ast is AlterTableAddTableElementStatement);
        Assert.True(alter.InTryBlock);
        Assert.False(alter.InCatchBlock);
        Assert.Equal(1, alter.Depth);
        Assert.Same(tryCatch, alter.Parent);

        var rollback = Assert.Single(statements, s => s.Ast is RollbackTransactionStatement);
        Assert.True(rollback.InCatchBlock);
        Assert.False(rollback.InTryBlock);
        Assert.Equal(2, rollback.Depth); // TRY/CATCH → IF → ROLLBACK
        Assert.NotNull(rollback.EnclosingIf);

        var throwStatement = Assert.Single(statements, s => s.Ast is ThrowStatement);
        Assert.True(throwStatement.InCatchBlock);
        Assert.Null(throwStatement.EnclosingIf);
    }

    [Fact]
    public void Nested_try_inside_catch_reports_the_innermost_block()
    {
        var statements = Parse("""
            BEGIN TRY
                SELECT 1;
            END TRY
            BEGIN CATCH
                BEGIN TRY
                    SELECT 2;
                END TRY
                BEGIN CATCH
                    SELECT 3;
                END CATCH
            END CATCH
            """);

        var selects = statements.Where(s => s.Ast is SelectStatement).ToList();
        Assert.Equal(3, selects.Count);

        Assert.True(selects[0].InTryBlock);
        Assert.False(selects[0].InCatchBlock);

        Assert.True(selects[1].InTryBlock);
        Assert.False(selects[1].InCatchBlock);

        Assert.False(selects[2].InTryBlock);
        Assert.True(selects[2].InCatchBlock);
    }

    [Fact]
    public void While_body_is_flagged()
    {
        var statements = Parse("""
            DECLARE @i int = 0;
            WHILE @i < 10
            BEGIN
                DELETE TOP (1000) FROM dbo.T WHERE Archived = 1;
                SET @i += 1;
            END
            """);

        var delete = Assert.Single(statements, s => s.Ast is DeleteStatement);
        Assert.True(delete.InWhileLoop);
        Assert.Equal(2, delete.Depth);
        Assert.Null(delete.EnclosingIf);

        var declare = statements[0];
        Assert.False(declare.InWhileLoop);
        Assert.Equal(0, declare.Depth);
    }

    [Theory]
    [InlineData("CREATE PROCEDURE dbo.P AS BEGIN DELETE FROM dbo.T; END")]
    [InlineData("CREATE OR ALTER PROCEDURE dbo.P AS BEGIN IF 1 = 1 DELETE FROM dbo.T; END")]
    [InlineData("ALTER PROCEDURE dbo.P AS DELETE FROM dbo.T;")]
    [InlineData("CREATE FUNCTION dbo.F() RETURNS int AS BEGIN DECLARE @x int; SELECT @x = 1; RETURN @x; END")]
    [InlineData("CREATE TRIGGER dbo.TR ON dbo.T AFTER INSERT AS BEGIN DELETE FROM dbo.Log; END")]
    [InlineData("CREATE VIEW dbo.V AS SELECT Id FROM dbo.T;")]
    public void Module_bodies_are_not_flattened(string sql)
    {
        var statements = Parse(sql);

        var module = Assert.Single(statements);
        Assert.Equal(StatementKind.Ddl, module.Kind);
        Assert.Equal(0, module.Depth);
    }

    [Fact]
    public void Batch_index_follows_go_separators()
    {
        var result = ParseResult("""
            SELECT 1;
            GO
            IF 1 = 1
                SELECT 2;
            GO
            SELECT 3;
            """);

        var statements = result.Statements;
        Assert.Equal([0, 1, 1, 2], statements.Select(s => s.BatchIndex));

        Assert.Equal(3, result.Batches.Count);
        Assert.Equal(0, result.Batches[0].Index);
        Assert.Equal(0, result.Batches[0].FirstStatementIndex);
        Assert.Equal([0], result.Batches[0].StatementIndices);

        Assert.Equal(1, result.Batches[1].Index);
        Assert.Equal(1, result.Batches[1].FirstStatementIndex);
        Assert.Equal([1, 2], result.Batches[1].StatementIndices); // nested SELECT belongs to the batch too

        Assert.Equal(2, result.Batches[2].Index);
        Assert.Equal(3, result.Batches[2].FirstStatementIndex);
        Assert.Equal([3], result.Batches[2].StatementIndices);
    }

    [Fact]
    public void Index_and_batch_offsets_keep_numbering_global_across_files()
    {
        var result = new MsSqlScriptParser().Parse(
            "SELECT 1;\nGO\nSELECT 2;", "two.sql", SqlServerVersion.Sql2019,
            indexOffset: 5, batchIndexOffset: 3);

        Assert.Equal([5, 6], result.Statements.Select(s => s.Index));
        Assert.Equal([3, 4], result.Statements.Select(s => s.BatchIndex));
        Assert.Equal([3, 4], result.Batches.Select(b => b.Index));
        Assert.Equal([5, 6], result.Batches.Select(b => b.FirstStatementIndex));
    }

    [Fact]
    public void Index_is_assigned_in_pre_order()
    {
        var statements = Parse("""
            IF 1 = 1
            BEGIN
                SELECT 1;
                IF 2 = 2 SELECT 2;
            END
            SELECT 3;
            """);

        Assert.Equal(Enumerable.Range(0, 6), statements.Select(s => s.Index));
        Assert.Equal(
            ["IfStatement", "BeginEndBlockStatement", "SelectStatement", "IfStatement", "SelectStatement", "SelectStatement"],
            statements.Select(s => s.Ast.GetType().Name));
        Assert.Equal([0, 1, 2, 2, 3, 0], statements.Select(s => s.Depth));
    }

    [Fact]
    public void Nested_statements_inherit_suppressions_placed_on_the_enclosing_block()
    {
        var statements = Parse("""
            -- planizer:ignore MSSQL-LOCK-001 guarded, reviewed
            IF COL_LENGTH('dbo.T', 'C') IS NULL
            BEGIN
                ALTER TABLE dbo.T ADD C int NULL;
            END
            """);

        var alter = Assert.Single(statements, s => s.Ast is AlterTableAddTableElementStatement);
        Assert.Contains("MSSQL-LOCK-001", alter.SuppressedRuleIds);
        Assert.Equal("guarded, reviewed", alter.SuppressReason);
    }

    [Fact]
    public void Analyzer_sees_nested_ddl_and_counts_it()
    {
        var report = new MsSqlAnalyzer(rules: []).Analyze(
            [
                ("migration.sql", """
                    IF COL_LENGTH('dbo.T', 'C') IS NULL
                    BEGIN
                        ALTER TABLE dbo.T ADD C int NULL;
                    END
                    GO
                    SELECT 1;
                    """),
            ],
            new PlanizerConfig());

        // IF + BEGIN-END wrappers + ALTER + SELECT
        Assert.Equal(4, report.Summary.StatementCount);
        Assert.Equal(1, report.Summary.DdlCount);
        Assert.Equal(1, report.Summary.SchMLockCount);
    }

    [Fact]
    public void Context_exposes_batches_and_per_batch_and_per_file_lookups()
    {
        MsSqlAnalysisContext? seen = null;
        new MsSqlAnalyzer(rules: [new ContextProbeRule(c => seen = c)]).Analyze(
            [
                ("one.sql", "SELECT 1;\nGO\nIF 1 = 1 SELECT 2;"),
                ("two.sql", "SELECT 3;"),
            ],
            new PlanizerConfig());

        Assert.NotNull(seen);
        Assert.Equal([0, 1, 2], seen.Batches.Select(b => b.Index));
        Assert.Equal([0, 1, 3], seen.Batches.Select(b => b.FirstStatementIndex));

        Assert.Equal([1, 2], seen.StatementsInBatch(1).Select(s => s.Index));
        Assert.Equal([3], seen.StatementsInBatch(2).Select(s => s.Index));
        Assert.Empty(seen.StatementsInBatch(42));

        Assert.Equal([0, 1, 2], seen.StatementsInFile("one.sql").Select(s => s.Index));
        Assert.Equal([3], seen.StatementsInFile("two.sql").Select(s => s.Index));
        Assert.Equal(2, seen.StatementsInFile("two.sql").Single().BatchIndex); // batch numbering is global too

        Assert.NotNull(seen.Features);
        Assert.NotNull(seen.Features.Lookup("STRING_AGG")); // the live catalog, not a stub
    }

    private static IReadOnlyList<SqlStatementInfo> Parse(string sql) => ParseResult(sql).Statements;

    private static MsSqlParseResult ParseResult(string sql)
    {
        var result = new MsSqlScriptParser().Parse(sql, "test.sql", SqlServerVersion.Sql2019);
        Assert.Empty(result.Errors);
        return result;
    }

    private sealed class ContextProbeRule(Action<MsSqlAnalysisContext> onAnalyze) : MsSqlRuleBase
    {
        public override string Id => "TEST-PROBE-002";
        public override string Title => "Test rule: captures the analysis context";
        public override Severity DefaultSeverity => Severity.Info;

        protected override IEnumerable<Finding> Analyze(MsSqlAnalysisContext context)
        {
            onAnalyze(context);
            yield break;
        }
    }
}
