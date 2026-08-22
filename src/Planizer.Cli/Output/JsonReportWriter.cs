using System.Text.Json;
using System.Text.Json.Serialization;
using Planizer.Core;

namespace Planizer.Cli.Output;

/// <summary>Machine-readable report: the <see cref="Report"/> as camelCase JSON, enums as strings.</summary>
public sealed class JsonReportWriter : IReportWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public void Write(Report report, TextWriter output)
        => output.WriteLine(JsonSerializer.Serialize(report, SerializerOptions));
}
