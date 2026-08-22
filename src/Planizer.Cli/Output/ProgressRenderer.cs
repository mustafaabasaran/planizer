using System.Diagnostics;
using Planizer.Core;

namespace Planizer.Cli.Output;

/// <summary>
/// One-line, in-place progress indicator on stderr: <c>planizer | parsing 12/47  file.sql</c>,
/// then <c>rules 23/53  MSSQL-TRAN-003</c>, then <c>finishing</c>. Never touches stdout, so
/// json/sarif/markdown output stays pipeable; the line is erased on <see cref="Dispose"/> before
/// the report is written. Renders are throttled to <c>minInterval</c> except for the first and
/// last tick of each phase, so thousands of files do not flood the terminal.
/// </summary>
public sealed class ProgressRenderer : IProgress<AnalysisProgress>, IDisposable
{
    private const string ClearLine = "\r\x1b[2K";
    private static readonly char[] Spinner = ['-', '\\', '|', '/'];

    private readonly TextWriter _error;
    private readonly TimeSpan _minInterval;
    private readonly int _width;

    private long _lastRenderTimestamp;
    private AnalysisPhase? _lastPhase;
    private int _spin;
    private bool _rendered;

    /// <param name="error">Where to draw (stderr).</param>
    /// <param name="minInterval">Minimum time between two redraws; <see cref="TimeSpan.Zero"/> renders every tick.</param>
    /// <param name="width">Terminal width; longer lines are truncated so they never wrap.</param>
    public ProgressRenderer(TextWriter error, TimeSpan minInterval, int width = 80)
    {
        _error = error;
        _minInterval = minInterval;
        _width = Math.Max(20, width);
    }

    /// <summary>Shown only on an interactive terminal: a redirected stderr (CI log, file) gets nothing.</summary>
    public static bool ShouldShow(bool noProgressFlag, bool errorRedirected)
        => !noProgressFlag && !errorRedirected;

    public void Report(AnalysisProgress value)
    {
        var phaseChanged = _lastPhase != value.Phase;
        var edge = value.Current <= 1 || value.Current >= value.Total;
        var due = _minInterval == TimeSpan.Zero
                  || _lastRenderTimestamp == 0
                  || Stopwatch.GetElapsedTime(_lastRenderTimestamp) >= _minInterval;

        if (!phaseChanged && !edge && !due)
        {
            return;
        }

        _lastPhase = value.Phase;
        _lastRenderTimestamp = Stopwatch.GetTimestamp();
        _rendered = true;

        var spinner = Spinner[_spin++ % Spinner.Length];
        var text = value.Phase switch
        {
            AnalysisPhase.Parsing => $"planizer {spinner} parsing {value.Current}/{value.Total}  {value.Label}",
            AnalysisPhase.Rules => $"planizer {spinner} rules {value.Current}/{value.Total}  {value.Label}",
            _ => $"planizer {spinner} finishing",
        };

        if (text.Length >= _width)
        {
            text = text[..(_width - 2)] + "…";
        }

        _error.Write(ClearLine + text);
        _error.Flush();
    }

    /// <summary>Erases the progress line so the report starts on a clean line.</summary>
    public void Dispose()
    {
        if (_rendered)
        {
            _error.Write(ClearLine);
            _error.Flush();
            _rendered = false;
        }
    }
}
