using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

/// <summary>Canonical ingestion inventory.</summary>
public sealed record DocumentIngestionContract
{
    /// <summary>Gets the configure-only schedule name.</summary>
    public required string Scenario
    {
        get; init;
    }
    /// <summary>Gets exact total records across all clients.</summary>
    public required int Records
    {
        get; init;
    }
    /// <summary>Gets actual admitted client counts.</summary>
    public required ImmutableArray<int> Clients
    {
        get; init;
    }
}
