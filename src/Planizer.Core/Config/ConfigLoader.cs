using System.Text.Json;
using System.Text.Json.Serialization;

namespace Planizer.Core;

/// <summary>
/// Loads <c>.planizer.json</c> into a <see cref="PlanizerConfig"/> (camelCase properties, enums as
/// strings) and layers CLI overrides on top of the file values.
/// </summary>
public static class ConfigLoader
{
    public const string DefaultFileName = ".planizer.json";

    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    /// <summary>Reads and parses a config file. Throws <see cref="FileNotFoundException"/> when missing.</summary>
    public static PlanizerConfig LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Config file not found: {path}", path);
        }

        return Parse(File.ReadAllText(path));
    }

    /// <summary>Parses config JSON. Unknown rule ids are kept as-is; they are not an error.</summary>
    public static PlanizerConfig Parse(string json)
        => JsonSerializer.Deserialize<PlanizerConfig>(json, SerializerOptions) ?? new PlanizerConfig();

    /// <summary>CLI arguments win over config-file values; <c>null</c> means "not given on the CLI".</summary>
    public static PlanizerConfig ApplyOverrides(
        PlanizerConfig config,
        SqlDialect? dialect = null,
        SqlServerVersion? targetVersion = null,
        SqlEdition? edition = null,
        Severity? failOn = null,
        bool? rollback = null)
        => config with
        {
            Dialect = dialect ?? config.Dialect,
            Rollback = rollback ?? config.Rollback,
            TargetVersion = targetVersion ?? config.TargetVersion,
            Edition = edition ?? config.Edition,
            FailOn = failOn ?? config.FailOn,
        };

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        // Specific converters first: they accept the user-facing tokens ("2019", "azure",
        // "developer") in addition to enum member names.
        options.Converters.Add(new SqlServerVersionConverter());
        options.Converters.Add(new SqlEditionConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class SqlServerVersionConverter : JsonConverter<SqlServerVersion>
    {
        public override SqlServerVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (TargetParser.TryParseVersion(value, out var version))
            {
                return version;
            }

            // Also accept enum member names (e.g. "Sql2017"), but never bare numbers:
            // Enum.TryParse would happily turn "2015" into an undefined enum value.
            var trimmed = value?.Trim();
            if (!string.IsNullOrEmpty(trimmed)
                && !char.IsAsciiDigit(trimmed[0])
                && Enum.TryParse<SqlServerVersion>(trimmed, ignoreCase: true, out var named)
                && Enum.IsDefined(named))
            {
                return named;
            }

            throw new JsonException(
                $"Invalid targetVersion '{value}'. Accepted values: 2014, 2016, 2017, 2019, 2022, azure.");
        }

        public override void Write(Utf8JsonWriter writer, SqlServerVersion value, JsonSerializerOptions options)
            => writer.WriteStringValue(TargetParser.VersionToken(value));
    }

    private sealed class SqlEditionConverter : JsonConverter<SqlEdition>
    {
        public override SqlEdition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (TargetParser.TryParseEdition(value, out var edition))
            {
                return edition;
            }

            throw new JsonException(
                $"Invalid edition '{value}'. Accepted values: enterprise, standard, express, azure, developer.");
        }

        public override void Write(Utf8JsonWriter writer, SqlEdition value, JsonSerializerOptions options)
            => writer.WriteStringValue(TargetParser.EditionToken(value));
    }
}
