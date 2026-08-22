using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

public class TransactionScopeTests
{
    [Fact]
    public void Two_sequential_transactions_produce_two_scopes()
    {
        var statements = Parse("""
            BEGIN TRAN;
            ALTER TABLE dbo.A ADD C1 int NULL;
            COMMIT;
            BEGIN TRAN;
            ALTER TABLE dbo.B ADD C1 int NULL;
            COMMIT;
            """);

        var scopes = TransactionScopeBuilder.Build(statements);

        Assert.Equal(2, scopes.Count);
        AssertScope(scopes[0], beginIndex: 0, endIndex: 2, statementIndices: [1]);
        AssertScope(scopes[1], beginIndex: 3, endIndex: 5, statementIndices: [4]);
    }

    [Fact]
    public void Unclosed_transaction_extends_to_the_end_of_the_script()
    {
        var statements = Parse("""
            BEGIN TRAN;
            ALTER TABLE dbo.A ADD C1 int NULL;
            DROP TABLE dbo.B;
            """);

        var scopes = TransactionScopeBuilder.Build(statements);

        var scope = Assert.Single(scopes);
        Assert.Equal(0, scope.BeginIndex);
        Assert.Equal(2, scope.EndIndex);
        Assert.Equal([1, 2], scope.StatementIndices);
    }

    [Fact]
    public void Script_without_transactions_has_no_scopes()
    {
        var statements = Parse("ALTER TABLE dbo.A ADD C1 int NULL;\nDROP TABLE dbo.B;");

        Assert.Empty(TransactionScopeBuilder.Build(statements));
    }

    [Fact]
    public void Rollback_closes_a_scope_like_commit()
    {
        var statements = Parse("""
            BEGIN TRAN;
            DELETE FROM dbo.A;
            ROLLBACK;
            """);

        var scope = Assert.Single(TransactionScopeBuilder.Build(statements));
        AssertScope(scope, beginIndex: 0, endIndex: 2, statementIndices: [1]);
    }

    [Fact]
    public void Nested_begin_tran_stays_inside_the_outer_scope()
    {
        var statements = Parse("""
            BEGIN TRAN;
            BEGIN TRAN;
            DELETE FROM dbo.A;
            COMMIT;
            COMMIT;
            """);

        var scope = Assert.Single(TransactionScopeBuilder.Build(statements));
        Assert.Equal(0, scope.BeginIndex);
        Assert.Equal(4, scope.EndIndex);
        Assert.Equal([1, 2, 3], scope.StatementIndices);
    }

    [Fact]
    public void Context_reports_whether_a_statement_is_in_an_explicit_transaction()
    {
        var statements = Parse("""
            SELECT 1;
            BEGIN TRAN;
            ALTER TABLE dbo.A ADD C1 int NULL;
            COMMIT;
            SELECT 2;
            """);
        var context = new MsSqlAnalysisContext
        {
            Mode = AnalysisMode.Offline,
            Config = new PlanizerConfig(),
            Schema = UnavailableSchemaProvider.Instance,
            Stats = UnavailableStatsProvider.Instance,
            AssumptionText = "SQL Server 2019, Standard edition, offline mode",
            Statements = statements,
            Transactions = TransactionScopeBuilder.Build(statements),
            Catalog = DdlBehaviorCatalog.Load(),
            Batches = [],
            Features = FeatureVersionCatalog.Load(),
        };

        Assert.False(context.IsInExplicitTransaction(0));
        Assert.True(context.IsInExplicitTransaction(2));
        Assert.False(context.IsInExplicitTransaction(4));
    }

    private static void AssertScope(
        TransactionScope scope,
        int beginIndex,
        int endIndex,
        int[] statementIndices)
    {
        Assert.Equal(beginIndex, scope.BeginIndex);
        Assert.Equal(endIndex, scope.EndIndex);
        Assert.Equal(statementIndices, scope.StatementIndices);
    }

    private static IReadOnlyList<SqlStatementInfo> Parse(string sql)
    {
        var result = new MsSqlScriptParser().Parse(sql, "test.sql", SqlServerVersion.Sql2019);
        Assert.Empty(result.Errors);
        return result.Statements;
    }
}
