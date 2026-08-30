using System.Text.Json;
using System.Text.Json.Serialization;
using Planizer.Core;

namespace Planizer.Cli.Output;

/// <summary>Machine-readable report: the <see cref="Report"/> as camelCase JSON, enums as strings.</summary>
public sealed class JsonReportWriter : IReportWriter
{
    public void Write(Report report, TextWriter output)
        => output.WriteLine(JsonSerializer.Serialize(report, ReportJsonContext.Default.Report));
}

/// <summary>
/// Source-generated serialization so the JSON output survives Native AOT (reflection-based
/// <see cref="JsonSerializer"/> needs runtime code generation and is trimmed away).
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Report))]
internal sealed partial class ReportJsonContext : JsonSerializerContext;
