using Planizer.Core;
using Planizer.MsSql;

namespace Planizer.Tests;

public class SuppressionTests
{
    [Fact]
    public void Directive_on_the_statement_line_attaches()
    {
        var statement = Parse("DROP TABLE dbo.Old; -- planizer:ignore MSSQL-LOCK-001 approved by dba").Single();

        Assert.Contains("MSSQL-LOCK-001", statement.SuppressedRuleIds);
        Assert.Equal("approved by dba", statement.SuppressReason);
    }

    [Fact]
    public void Directive_on_the_previous_line_attaches()
    {
        var statements = Parse("-- planizer:ignore MSSQL-LOCK-001\nDROP TABLE dbo.Old;");

        var statement = statements.Single();
        Assert.Contains("MSSQL-LOCK-001", statement.SuppressedRuleIds);
        Assert.Null(statement.SuppressReason);
    }

    [Fact]
    public void Directive_with_two_comma_separated_rules_attaches_both()
    {
        var statement = Parse(
            "-- planizer:ignore MSSQL-LOCK-001, MSSQL-RW-002 migration window\nALTER TABLE dbo.T ADD C int NULL;").Single();

        Assert.Contains("MSSQL-LOCK-001", statement.SuppressedRuleIds);
        Assert.Contains("MSSQL-RW-002", statement.SuppressedRuleIds);
        Assert.Equal(2, statement.SuppressedRuleIds.Count);
        Assert.Equal("migration window", statement.SuppressReason);
    }

    [Fact]
    public void Directive_without_reason_has_null_reason()
    {
        var statement = Parse("DROP TABLE dbo.Old; -- planizer:ignore MSSQL-LOCK-001").Single();

        Assert.Contains("MSSQL-LOCK-001", statement.SuppressedRuleIds);
        Assert.Null(statement.SuppressReason);
    }

    [Fact]
    public void Unrelated_comment_does_not_attach()
    {
        var statement = Parse("-- drop the legacy table\nDROP TABLE dbo.Old;").Single();

        Assert.Empty(statement.SuppressedRuleIds);
        Assert.Null(statement.SuppressReason);
    }

    [Fact]
    public void Directive_two_lines_above_does_not_attach()
    {
        var statement = Parse("-- planizer:ignore MSSQL-LOCK-001\n\nDROP TABLE dbo.Old;").Single();

        Assert.Empty(statement.SuppressedRuleIds);
    }

    [Fact]
    public void Trailing_directive_does_not_leak_to_the_next_statement()
    {
        var statements = Parse("DROP TABLE dbo.A; -- planizer:ignore MSSQL-LOCK-001\nDROP TABLE dbo.B;");

        Assert.Contains("MSSQL-LOCK-001", statements[0].SuppressedRuleIds);
        Assert.Empty(statements[1].SuppressedRuleIds);
    }

    [Fact]
    public void Rule_id_match_is_case_insensitive()
    {
        var statement = Parse("-- planizer:ignore mssql-lock-001\nDROP TABLE dbo.Old;").Single();

        Assert.Contains("MSSQL-LOCK-001", statement.SuppressedRuleIds);
    }

    private static IReadOnlyList<SqlStatementInfo> Parse(string sql)
    {
        var result = new MsSqlScriptParser().Parse(sql, "test.sql", SqlServerVersion.Sql2019);
        Assert.Empty(result.Errors);
        return result.Statements;
    }
}
