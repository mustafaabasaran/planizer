namespace Planizer.Core;

/// <summary>Target SQL Server version. Numeric values match compatibility levels where applicable.</summary>
public enum SqlServerVersion
{
    Sql2014 = 120,
    Sql2016 = 130,
    Sql2017 = 140,
    Sql2019 = 150,
    Sql2022 = 160,
    AzureSql = 1000,
}
