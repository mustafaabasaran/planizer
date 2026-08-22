using Planizer.Core;

namespace Planizer.Cli.Output;

/// <summary>Renders a <see cref="Report"/> to one output format (text, json, markdown…).</summary>
public interface IReportWriter
{
    void Write(Report report, TextWriter output);
}
