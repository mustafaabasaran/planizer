using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planizer.Core;
using Planizer.MsSql;
using Planizer.MsSql.Parsing;

namespace Planizer.Tests;

/// <summary>
/// The feature → minimum version table behind MSSQL-VER-001. Every CSV row is exercised: AST-
/// detected rows through <see cref="Detected"/>, grammar-only rows through <see cref="GrammarGated"/>,
/// and <see cref="Every_catalog_row_is_covered_by_a_sample"/> fails when a row is added without one.
/// </summary>
public class FeatureVersionCatalogTests
{
    private static readonly FeatureVersionCatalog Catalog = FeatureVersionCatalog.Load();

    /// <summary>feature_key → sample SQL whose AST contains the feature (parsed with the newest grammar).</summary>
    public static TheoryData<string, string> Detected => new()
    {
        { "string_split", "SELECT value FROM STRING_SPLIT('a,b', ',');" },
        { "openjson", "SELECT * FROM OPENJSON(@j);" },
        { "json_value", "SELECT JSON_VALUE(@j, '$.a');" },
        { "json_query", "SELECT JSON_QUERY(@j, '$.a');" },
        { "json_modify", "SELECT JSON_MODIFY(@j, '$.a', 1);" },
        { "isjson", "SELECT ISJSON(@j);" },
        { "compress", "SELECT COMPRESS(@b);" },
        { "decompress", "SELECT DECOMPRESS(@b);" },
        { "datediff_big", "SELECT DATEDIFF_BIG(ms, @a, @b);" },
        { "string_escape", "SELECT STRING_ESCAPE(@s, 'json');" },
        { "session_context", "SELECT SESSION_CONTEXT(N'k');" },
        { "at_time_zone", "SELECT SYSDATETIME() AT TIME ZONE 'UTC';" },
        { "create_or_alter_procedure", "CREATE OR ALTER PROCEDURE dbo.P AS SELECT 1;" },
        { "create_or_alter_function", "CREATE OR ALTER FUNCTION dbo.F() RETURNS int AS BEGIN RETURN 1; END;" },
        { "create_or_alter_view", "CREATE OR ALTER VIEW dbo.V AS SELECT 1 AS X;" },
        { "create_or_alter_trigger", "CREATE OR ALTER TRIGGER dbo.TR ON dbo.T AFTER INSERT AS SELECT 1;" },
        { "string_agg", "SELECT STRING_AGG(Name, ',') FROM dbo.T;" },
        { "trim", "SELECT TRIM(Name) FROM dbo.T;" },
        { "concat_ws", "SELECT CONCAT_WS('-', A, B) FROM dbo.T;" },
        { "translate", "SELECT TRANSLATE(A, 'abc', 'xyz') FROM dbo.T;" },
        { "resumable_index_rebuild", "ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON, RESUMABLE = ON);" },
        { "resumable_create_index", "CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON, RESUMABLE = ON);" },
        { "approx_count_distinct", "SELECT APPROX_COUNT_DISTINCT(Id) FROM dbo.T;" },
        { "greatest", "SELECT GREATEST(A, B) FROM dbo.T;" },
        { "least", "SELECT LEAST(A, B) FROM dbo.T;" },
        { "date_bucket", "SELECT DATE_BUCKET(day, 1, CreatedAt) FROM dbo.T;" },
        { "datetrunc", "SELECT DATETRUNC(day, CreatedAt) FROM dbo.T;" },
        { "generate_series", "SELECT value FROM GENERATE_SERIES(1, 10);" },
        { "json_path_exists", "SELECT JSON_PATH_EXISTS(@j, '$.a');" },
        { "json_object", "SELECT JSON_OBJECT('a': 1);" },
        { "json_array", "SELECT JSON_ARRAY(1, 2);" },
        { "approx_percentile_cont", "SELECT APPROX_PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY Id) FROM dbo.T;" },
        { "approx_percentile_disc", "SELECT APPROX_PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY Id) FROM dbo.T;" },
        { "bit_count", "SELECT BIT_COUNT(Flags) FROM dbo.T;" },
        { "get_bit", "SELECT GET_BIT(Flags, 1) FROM dbo.T;" },
        { "set_bit", "SELECT SET_BIT(Flags, 1) FROM dbo.T;" },
        { "left_shift", "SELECT LEFT_SHIFT(Flags, 1) FROM dbo.T;" },
        { "right_shift", "SELECT RIGHT_SHIFT(Flags, 1) FROM dbo.T;" },
        { "ltrim_characters", "SELECT LTRIM(Name, 'x') FROM dbo.T;" },
        { "rtrim_characters", "SELECT RTRIM(Name, 'x') FROM dbo.T;" },
        { "string_split_ordinal", "SELECT value FROM STRING_SPLIT('a,b', ',', 1);" },
        { "wait_at_low_priority_create_index", "CREATE INDEX IX ON dbo.T (C) WITH (ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)));" },
        { "wait_at_low_priority_index_rebuild", "ALTER INDEX IX ON dbo.T REBUILD WITH (ONLINE = ON (WAIT_AT_LOW_PRIORITY (MAX_DURATION = 1 MINUTES, ABORT_AFTER_WAIT = SELF)));" },
    };

    /// <summary>feature_key → sample SQL whose feature only shows up under the 2014 grammar's AST shapes.</summary>
    public static TheoryData<string, string> LegacyGrammarDetected => new()
    {
        { "openjson_function", "SELECT * FROM OPENJSON(@j);" },
        { "string_split", "SELECT value FROM STRING_SPLIT('a,b', ',');" },
    };

    /// <summary>feature_key → sample SQL the grammar below min_version rejects and the min_version grammar accepts.</summary>
    public static TheoryData<string, string> GrammarGated => new()
    {
        { "drop_if_exists", "DROP TABLE IF EXISTS dbo.T;" },
        { "trim_leading_trailing", "SELECT TRIM(LEADING 'x' FROM Name) FROM dbo.T;" },
        { "is_distinct_from", "SELECT 1 FROM dbo.T WHERE A IS DISTINCT FROM B;" },
        { "window_clause", "SELECT SUM(A) OVER w FROM dbo.T WINDOW w AS (PARTITION BY B);" },
        { "ledger_table", "CREATE TABLE dbo.L (Id int NOT NULL) WITH (LEDGER = ON);" },
    };

    [Theory]
    [MemberData(nameof(Detected))]
    public void Catalogued_feature_is_detected_in_the_ast(string featureKey, string sql)
    {
        var feature = Catalog.Lookup(featureKey);
        Assert.NotNull(feature);
        Assert.NotEqual(FeatureDetection.Syntax, feature.Detection);

        var uses = Catalog.FindUses(Parse(sql, SqlGrammar.Preview));

        Assert.Contains(uses, u => u.Feature.FeatureKey == featureKey);
    }

    [Theory]
    [MemberData(nameof(LegacyGrammarDetected))]
    public void Catalogued_feature_is_detected_under_the_2014_grammar(string featureKey, string sql)
    {
        // TSql120 has no OpenJsonTableReference / GlobalFunctionTableReference: built-in
        // table-valued functions come through as SchemaObjectFunctionTableReference.
        var uses = Catalog.FindUses(Parse(sql, SqlGrammar.Sql2014));

        Assert.Contains(uses, u => u.Feature.FeatureKey == featureKey);
    }

    [Theory]
    [MemberData(nameof(GrammarGated))]
    public void Syntax_row_is_rejected_below_and_accepted_at_its_min_version(string featureKey, string sql)
    {
        var feature = Catalog.Lookup(featureKey);
        Assert.NotNull(feature);
        Assert.Equal(FeatureDetection.Syntax, feature.Detection);

        var minimum = SqlGrammar.For(feature.MinVersion);
        var previous = SqlGrammar.All.Last(g => g.Level < minimum.Level);

        Assert.NotEmpty(ParseErrors(sql, previous));
        Assert.Empty(ParseErrors(sql, minimum));
    }

    [Fact]
    public void Every_catalog_row_is_covered_by_a_sample()
    {
        var sampled = Detected.Select(row => (string)row[0])
            .Concat(LegacyGrammarDetected.Select(row => (string)row[0]))
            .Concat(GrammarGated.Select(row => (string)row[0]))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = Catalog.Features.Select(f => f.FeatureKey).Where(k => !sampled.Contains(k)).ToList();

        Assert.True(missing.Count == 0, "CSV rows without a test sample: " + string.Join(", ", missing));
        Assert.True(Catalog.Features.Count >= 40);
    }

    [Fact]
    public void Lookup_is_case_insensitive_and_null_for_unknown_keys()
    {
        var feature = Catalog.Lookup("STRING_AGG");

        Assert.NotNull(feature);
        Assert.Equal(SqlServerVersion.Sql2017, feature.MinVersion);
        Assert.Equal("2017", feature.MinVersionLabel);
        Assert.Equal("STRING_AGG()", feature.Label);
        Assert.Null(Catalog.Lookup("no_such_feature"));
    }

    [Theory]
    [InlineData(SqlServerVersion.Sql2014, false)]
    [InlineData(SqlServerVersion.Sql2016, false)]
    [InlineData(SqlServerVersion.Sql2017, true)]
    [InlineData(SqlServerVersion.Sql2022, true)]
    [InlineData(SqlServerVersion.AzureSql, true)]
    public void Feature_is_available_from_its_min_version_on(SqlServerVersion target, bool available)
    {
        var violations = Catalog.FindViolations(Parse("SELECT STRING_AGG(Name, ',') FROM dbo.T;"), target);

        Assert.Equal(available, !violations.Any());
    }

    [Fact]
    public void Service_pack_feature_is_not_satisfied_by_a_bare_2016_target()
    {
        var fragment = Parse("CREATE OR ALTER PROCEDURE dbo.P AS SELECT 1;");

        var on2016 = Assert.Single(Catalog.FindViolations(fragment, SqlServerVersion.Sql2016));
        Assert.True(on2016.Feature.RequiresServicePack1);
        Assert.Equal("2016 SP1", on2016.Feature.MinVersionLabel);
        Assert.Equal("CREATE OR ALTER PROCEDURE", on2016.Feature.Label);
        Assert.Empty(Catalog.FindViolations(fragment, SqlServerVersion.Sql2017));
    }

    [Fact]
    public void Most_demanding_feature_wins_when_several_rows_match_one_call()
    {
        // STRING_SPLIT (2016) and its 3-argument form (2022) both match; at 2014 only one finding.
        var violations = Catalog.FindViolations(
            Parse("SELECT value FROM STRING_SPLIT('a,b', ',', 1);"), SqlServerVersion.Sql2014).ToList();

        var violation = Assert.Single(violations);
        Assert.Equal("string_split_ordinal", violation.Feature.FeatureKey);
        Assert.Equal("STRING_SPLIT() with 3 arguments", violation.Feature.Label);
    }

    [Fact]
    public void Two_argument_form_does_not_match_the_one_argument_call()
    {
        Assert.Empty(Catalog.FindUses(Parse("SELECT LTRIM(Name) FROM dbo.T;")));
    }

    [Fact]
    public void Schema_qualified_call_is_a_user_function_not_a_builtin()
    {
        Assert.Empty(Catalog.FindUses(Parse("SELECT dbo.STRING_AGG(Name, ',') FROM dbo.T;")));
        Assert.Empty(Catalog.FindUses(Parse("SELECT * FROM dbo.STRING_SPLIT('a', ',');")));
    }

    [Fact]
    public void Procedure_bodies_are_scanned()
    {
        var uses = Catalog.FindUses(Parse("CREATE PROCEDURE dbo.P AS BEGIN SELECT STRING_AGG(A, ',') FROM dbo.T; END"));

        Assert.Contains(uses, u => u.Feature.FeatureKey == "string_agg");
    }

    [Fact]
    public void Parse_rejects_a_wrong_header()
    {
        Assert.Throws<InvalidDataException>(() => FeatureVersionCatalog.Parse("key,detect\nx,function"));
    }

    [Theory]
    [InlineData("x,regex,X,2016,")]
    [InlineData("x,function,X,2015,")]
    [InlineData("x,function,X/zero,2016,")]
    [InlineData("x,function,X,2016")]
    public void Parse_rejects_malformed_rows(string row)
    {
        Assert.Throws<InvalidDataException>(
            () => FeatureVersionCatalog.Parse("feature_key,detect,pattern,min_version,note\n" + row));
    }

    [Fact]
    public void Parse_reads_arity_and_note()
    {
        var catalog = FeatureVersionCatalog.Parse(
            "feature_key,detect,pattern,min_version,note\nltrim2,function,LTRIM/2,2022,second argument");

        var feature = Assert.Single(catalog.Features);
        Assert.Equal("LTRIM", feature.FunctionName);
        Assert.Equal(2, feature.MinArguments);
        Assert.Equal("second argument", feature.Note);
        Assert.Equal(SqlServerVersion.Sql2022, feature.MinVersion);
    }

    private static TSqlFragment Parse(string sql, SqlGrammar? grammar = null)
    {
        var result = new MsSqlScriptParser().Parse(sql, "test.sql", grammar ?? SqlGrammar.Preview);
        Assert.Empty(result.Errors);
        return result.Statements[0].Ast;
    }

    private static IReadOnlyList<MsSqlParseError> ParseErrors(string sql, SqlGrammar grammar)
        => new MsSqlScriptParser().Parse(sql, "test.sql", grammar).Errors;
}
