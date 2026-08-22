using System.Text.Json;
using Planizer.Core;

namespace Planizer.Tests;

public class ConfigTests
{
    [Fact]
    public void Parse_empty_object_returns_worst_case_defaults()
    {
        var config = ConfigLoader.Parse("{}");

        Assert.Equal(SqlDialect.MsSql, config.Dialect);
        Assert.Equal(SqlServerVersion.Sql2019, config.TargetVersion);
        Assert.Equal(SqlEdition.Standard, config.Edition);
        Assert.Equal(Severity.Critical, config.FailOn);
        Assert.Empty(config.Rules);
    }

    [Fact]
    public void Parse_reads_camel_case_properties_and_friendly_enum_tokens()
    {
        var config = ConfigLoader.Parse("""
            {
              "dialect": "MsSql",
              "targetVersion": "2022",
              "edition": "developer",
              "failOn": "warning"
            }
            """);

        Assert.Equal(SqlDialect.MsSql, config.Dialect);
        Assert.Equal(SqlServerVersion.Sql2022, config.TargetVersion);
        Assert.Equal(SqlEdition.Enterprise, config.Edition); // developer maps to Enterprise
        Assert.Equal(Severity.Warning, config.FailOn);
    }

    [Fact]
    public void Parse_accepts_azure_target_and_enum_member_names()
    {
        var config = ConfigLoader.Parse("""
            {
              "targetVersion": "azure",
              "edition": "azure"
            }
            """);

        Assert.Equal(SqlServerVersion.AzureSql, config.TargetVersion);
        Assert.Equal(SqlEdition.Azure, config.Edition);

        var byName = ConfigLoader.Parse("""{ "targetVersion": "Sql2017" }""");
        Assert.Equal(SqlServerVersion.Sql2017, byName.TargetVersion);
    }

    [Fact]
    public void Parse_unknown_rule_id_is_not_an_error()
    {
        var config = ConfigLoader.Parse("""
            {
              "rules": {
                "MSSQL-DOES-NOT-EXIST-999": { "enabled": false },
                "MSSQL-RW-002": { "severity": "Warning" }
              }
            }
            """);

        Assert.Equal(2, config.Rules.Count);

        var unknown = config.Rules["MSSQL-DOES-NOT-EXIST-999"];
        Assert.False(unknown.Enabled);
        Assert.Null(unknown.Severity);

        var overridden = config.Rules["MSSQL-RW-002"];
        Assert.True(overridden.Enabled); // default when omitted
        Assert.Equal(Severity.Warning, overridden.Severity);
    }

    [Fact]
    public void Parse_invalid_target_version_throws_json_exception()
    {
        Assert.Throws<JsonException>(() => ConfigLoader.Parse("""{ "targetVersion": "2015" }"""));
        Assert.Throws<JsonException>(() => ConfigLoader.Parse("""{ "edition": "datacenter" }"""));
    }

    [Fact]
    public void LoadFile_reads_config_from_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"planizer-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "targetVersion": "2016",
              "edition": "enterprise",
              "failOn": "blocker",
              "rules": { "MSSQL-LOCK-001": { "enabled": false } }
            }
            """);

        try
        {
            var config = ConfigLoader.LoadFile(path);

            Assert.Equal(SqlServerVersion.Sql2016, config.TargetVersion);
            Assert.Equal(SqlEdition.Enterprise, config.Edition);
            Assert.Equal(Severity.Blocker, config.FailOn);
            Assert.False(config.Rules["MSSQL-LOCK-001"].Enabled);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFile_missing_file_throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"planizer-missing-{Guid.NewGuid():N}.json");
        Assert.Throws<FileNotFoundException>(() => ConfigLoader.LoadFile(path));
    }

    [Fact]
    public void ApplyOverrides_cli_values_win_over_file_values()
    {
        var fromFile = ConfigLoader.Parse("""
            {
              "targetVersion": "2016",
              "edition": "enterprise",
              "failOn": "warning"
            }
            """);

        var effective = ConfigLoader.ApplyOverrides(
            fromFile,
            edition: SqlEdition.Express,
            failOn: Severity.Blocker);

        Assert.Equal(SqlEdition.Express, effective.Edition);
        Assert.Equal(Severity.Blocker, effective.FailOn);

        // Values not given on the CLI keep the file values.
        Assert.Equal(SqlServerVersion.Sql2016, effective.TargetVersion);
        Assert.Equal(SqlDialect.MsSql, effective.Dialect);
    }

    [Fact]
    public void ApplyOverrides_without_cli_values_keeps_config_unchanged()
    {
        var fromFile = ConfigLoader.Parse("""
            {
              "targetVersion": "2022",
              "edition": "express",
              "failOn": "info",
              "rules": { "MSSQL-RW-001": { "enabled": false } }
            }
            """);

        var effective = ConfigLoader.ApplyOverrides(fromFile);

        Assert.Equal(fromFile, effective);
        Assert.Same(fromFile.Rules, effective.Rules);
    }
}
