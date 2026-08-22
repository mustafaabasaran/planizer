namespace Planizer.Core;

/// <summary>Schema information source. Offline mode has none; snapshot/live modes provide one (Phase 2).</summary>
public interface ISchemaProvider
{
    bool IsAvailable { get; }
    /* extended in Phase 2 (snapshot / live modes) */
}
