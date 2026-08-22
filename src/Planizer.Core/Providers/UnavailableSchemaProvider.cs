namespace Planizer.Core;

/// <summary>Null object for offline mode: no schema data. Rules must go Inconclusive, not silent.</summary>
public sealed class UnavailableSchemaProvider : ISchemaProvider
{
    public static UnavailableSchemaProvider Instance { get; } = new();

    private UnavailableSchemaProvider()
    {
    }

    public bool IsAvailable => false;
}
