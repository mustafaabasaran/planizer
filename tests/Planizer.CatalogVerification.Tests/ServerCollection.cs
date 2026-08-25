namespace Planizer.CatalogVerification.Tests;

/// <summary>
/// One SQL Server container per test run: every server-backed test class joins this collection,
/// which also serializes them — two class fixtures would start two concurrent containers per CI
/// matrix job for no benefit.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ServerCollection : ICollectionFixture<ServerFixture>
{
    public const string Name = "catalog-verification-server";
}
