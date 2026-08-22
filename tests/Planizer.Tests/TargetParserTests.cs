using Planizer.Core;

namespace Planizer.Tests;

public class TargetParserTests
{
    [Theory]
    [InlineData("2014", SqlServerVersion.Sql2014)]
    [InlineData("2016", SqlServerVersion.Sql2016)]
    [InlineData("2017", SqlServerVersion.Sql2017)]
    [InlineData("2019", SqlServerVersion.Sql2019)]
    [InlineData("2022", SqlServerVersion.Sql2022)]
    [InlineData("azure", SqlServerVersion.AzureSql)]
    [InlineData("AZURE", SqlServerVersion.AzureSql)]
    [InlineData(" 2019 ", SqlServerVersion.Sql2019)]
    public void ParseVersion_maps_known_tokens(string input, SqlServerVersion expected)
    {
        Assert.Equal(expected, TargetParser.ParseVersion(input));
    }

    [Theory]
    [InlineData("2015")]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseVersion_rejects_unknown_tokens(string? input)
    {
        Assert.Throws<ArgumentException>(() => TargetParser.ParseVersion(input));
    }

    [Theory]
    [InlineData("enterprise", SqlEdition.Enterprise)]
    [InlineData("standard", SqlEdition.Standard)]
    [InlineData("express", SqlEdition.Express)]
    [InlineData("azure", SqlEdition.Azure)]
    [InlineData("Standard", SqlEdition.Standard)]
    [InlineData("EXPRESS", SqlEdition.Express)]
    public void ParseEdition_maps_known_tokens(string input, SqlEdition expected)
    {
        Assert.Equal(expected, TargetParser.ParseEdition(input));
    }

    [Theory]
    [InlineData("developer")]
    [InlineData("Developer")]
    public void ParseEdition_maps_developer_to_enterprise(string input)
    {
        Assert.Equal(SqlEdition.Enterprise, TargetParser.ParseEdition(input));
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("datacenter")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseEdition_rejects_unknown_tokens(string? input)
    {
        Assert.Throws<ArgumentException>(() => TargetParser.ParseEdition(input));
    }

    [Theory]
    [InlineData(SqlServerVersion.Sql2014, "2014")]
    [InlineData(SqlServerVersion.Sql2016, "2016")]
    [InlineData(SqlServerVersion.Sql2017, "2017")]
    [InlineData(SqlServerVersion.Sql2019, "2019")]
    [InlineData(SqlServerVersion.Sql2022, "2022")]
    [InlineData(SqlServerVersion.AzureSql, "azure")]
    public void VersionToken_round_trips_every_version(SqlServerVersion version, string expected)
    {
        Assert.Equal(expected, TargetParser.VersionToken(version));
        Assert.Equal(version, TargetParser.ParseVersion(expected));
    }

    [Theory]
    [InlineData(SqlEdition.Enterprise, "Enterprise")]
    [InlineData(SqlEdition.Standard, "Standard")]
    [InlineData(SqlEdition.Express, "Express")]
    [InlineData(SqlEdition.Azure, "Azure")]
    public void EditionToken_round_trips_every_edition(SqlEdition edition, string expected)
    {
        Assert.Equal(expected, TargetParser.EditionToken(edition));
        Assert.Equal(edition, TargetParser.ParseEdition(expected));
    }
}
