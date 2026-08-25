using Microsoft.Data.SqlClient;
using Planizer.Core;
using Testcontainers.MsSql;

namespace Planizer.CatalogVerification.Tests;

/// <summary>
/// The SQL Server the verification job measures against. Environment contract:
/// <list type="bullet">
/// <item><c>PLANIZER_VERIFY_CONNSTR</c> — use this server instead of starting a container;</item>
/// <item><c>PLANIZER_VERIFY_IMAGE</c> — container image (default
/// <c>mcr.microsoft.com/mssql/server:2022-latest</c>);</item>
/// <item><c>PLANIZER_VERIFY_PID</c> — <c>MSSQL_PID</c> for the container (Developer or Express);
/// the fixture asserts via <c>SERVERPROPERTY</c> that the server really runs that edition.</item>
/// </list>
/// Outside <see cref="VerificationGate"/> (i.e. on every development machine) initialization is
/// a hard no-op: no container is started and no connection is opened, ever.
/// </summary>
public sealed class ServerFixture : IAsyncLifetime
{
    public const string DefaultImage = "mcr.microsoft.com/mssql/server:2022-latest";
    public const string VerifyDatabaseName = "planizer_verify";

    private MsSqlContainer? _container;
    private string? _connectionString;
    private SqlEdition? _edition;
    private SqlServerVersion? _version;
    private string? _editionDescription;

    public string ConnectionString => _connectionString ?? throw NotInitialized();

    /// <summary>Catalog edition of the running server; Developer reports as Enterprise.</summary>
    public SqlEdition Edition => _edition ?? throw NotInitialized();

    /// <summary>Catalog version of the running server (from <c>ProductMajorVersion</c>).</summary>
    public SqlServerVersion Version => _version ?? throw NotInitialized();

    /// <summary>The literal <c>SERVERPROPERTY('Edition')</c> string, for the report header.</summary>
    public string EditionDescription => _editionDescription ?? throw NotInitialized();

    public async Task InitializeAsync()
    {
        // HARD GATE: without PLANIZER_CATALOG_VERIFY=1 this fixture must never touch Docker or
        // any SQL Server. xunit constructs class fixtures even when every test is skipped, so
        // the gate lives here, not only in [VerifyFact].
        if (!VerificationGate.IsEnabled)
        {
            return;
        }

        var externalConnectionString = Environment.GetEnvironmentVariable("PLANIZER_VERIFY_CONNSTR");
        string serverConnectionString;
        if (!string.IsNullOrWhiteSpace(externalConnectionString))
        {
            serverConnectionString = externalConnectionString;
        }
        else
        {
            var image = Environment.GetEnvironmentVariable("PLANIZER_VERIFY_IMAGE") ?? DefaultImage;
            var pid = Environment.GetEnvironmentVariable("PLANIZER_VERIFY_PID") ?? "Developer";
            _container = new MsSqlBuilder(image)
                .WithEnvironment("MSSQL_PID", pid)
                .Build();
            await _container.StartAsync();
            serverConnectionString = _container.GetConnectionString();
        }

        _connectionString = await PrepareDatabaseAsync(serverConnectionString);
        await ReadAndAssertServerIdentityAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static InvalidOperationException NotInitialized() => new(
        "ServerFixture is not initialized: catalog verification is gated behind " +
        $"{VerificationGate.GateVariable}=1 and never runs on development machines.");

    /// <summary>
    /// Probes run in a dedicated <c>planizer_verify</c> database rather than master. When an
    /// external connection string already names a non-master database, that database is used
    /// as-is (the caller may not have CREATE DATABASE permission).
    /// </summary>
    private static async Task<string> PrepareDatabaseAsync(string serverConnectionString)
    {
        var builder = new SqlConnectionStringBuilder(serverConnectionString);
        if (!string.IsNullOrEmpty(builder.InitialCatalog)
            && !string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase))
        {
            return builder.ConnectionString;
        }

        await using var connection = new SqlConnection(serverConnectionString);
        await connection.OpenAsync();
        await Measurement.ExecuteAsync(
            connection,
            $"IF DB_ID('{VerifyDatabaseName}') IS NULL CREATE DATABASE [{VerifyDatabaseName}];",
            Measurement.LongCommandTimeoutSeconds);

        builder.InitialCatalog = VerifyDatabaseName;
        return builder.ConnectionString;
    }

    private async Task ReadAndAssertServerIdentityAsync()
    {
        await using var connection = await OpenConnectionAsync();
        using var command = new SqlCommand("""
            SELECT CAST(SERVERPROPERTY('EngineEdition') AS int),
                   CAST(SERVERPROPERTY('Edition') AS nvarchar(128)),
                   CAST(SERVERPROPERTY('ProductMajorVersion') AS nvarchar(128));
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("SERVERPROPERTY query returned no row.");
        }

        var engineEdition = reader.GetInt32(0);
        _editionDescription = reader.GetString(1);
        var majorVersion = reader.GetString(2);

        _edition = engineEdition switch
        {
            2 => SqlEdition.Standard,
            3 => SqlEdition.Enterprise, // Developer also reports EngineEdition 3: Enterprise behavior
            4 => SqlEdition.Express,
            5 or 8 => SqlEdition.Azure,
            _ => throw new InvalidOperationException(
                $"Unsupported EngineEdition {engineEdition} ({_editionDescription})."),
        };

        _version = majorVersion switch
        {
            "12" => SqlServerVersion.Sql2014,
            "13" => SqlServerVersion.Sql2016,
            "14" => SqlServerVersion.Sql2017,
            "15" => SqlServerVersion.Sql2019,
            "16" => SqlServerVersion.Sql2022,
            _ => SqlServerVersion.Sql2022, // future majors: treat as the newest cataloged version
        };

        AssertRequestedPid(_edition.Value, _editionDescription);
    }

    private static void AssertRequestedPid(SqlEdition actual, string description)
    {
        var requestedPid = Environment.GetEnvironmentVariable("PLANIZER_VERIFY_PID");
        if (string.IsNullOrWhiteSpace(requestedPid))
        {
            return;
        }

        var expected = requestedPid.Trim().ToLowerInvariant() switch
        {
            "developer" or "enterprise" => SqlEdition.Enterprise,
            "standard" => SqlEdition.Standard,
            "express" => SqlEdition.Express,
            _ => (SqlEdition?)null,
        };

        if (expected is null)
        {
            throw new InvalidOperationException(
                $"Unsupported PLANIZER_VERIFY_PID '{requestedPid}'. Use Developer, Enterprise, Standard, or Express.");
        }

        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"Requested PID '{requestedPid}' but the server reports '{description}' (mapped to {actual}). " +
                "Refusing to verify catalog rows against the wrong edition.");
        }
    }
}
