using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Contains one authorized shortest path and its committed read cut.</summary>
/// <param name="Version">Identifies the result contract version.</param>
/// <param name="Found">Indicates whether a path was found.</param>
/// <param name="Hops">Contains the path length, or null when no path exists.</param>
/// <param name="Vertices">Contains full entity references in path order.</param>
/// <param name="Edges">Contains projected edges in path order.</param>
/// <param name="CutPosition">Identifies the store position observed in the read gate.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphShortestPathContractAliases.Result)]
public sealed record GraphShortestPathResult(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] bool Found,
    [property: Orleans.Id(2)] int? Hops,
    [property: Orleans.Id(3)] ImmutableArray<EntityRef> Vertices,
    [property: Orleans.Id(4)] ImmutableArray<EdgeRecord> Edges,
    [property: Orleans.Id(5)] long CutPosition);
