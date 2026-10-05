using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Requests a bounded shortest directed path inside one graph partition.</summary>
/// <param name="Version">Identifies the versioned path contract.</param>
/// <param name="Partition">Identifies the atomic partition.</param>
/// <param name="Graph">Identifies the persisted graph resource.</param>
/// <param name="From">Identifies the visible source entity.</param>
/// <param name="To">Identifies the target entity.</param>
/// <param name="MaxDepth">Bounds path length in directed edges.</param>
/// <param name="MaxVertices">Bounds distinct visible vertices discovered.</param>
/// <param name="MaxEdges">Bounds examined adjacency edges.</param>
/// <param name="Labels">Optionally restricts traversed edge labels.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphShortestPathContractAliases.Request)]
public sealed record GraphShortestPathRequest(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] PartitionRef Partition,
    [property: Orleans.Id(2)] string Graph,
    [property: Orleans.Id(3)] EntityRef From,
    [property: Orleans.Id(4)] EntityRef To,
    [property: Orleans.Id(5)] int MaxDepth = 16,
    [property: Orleans.Id(6)] int MaxVertices = 1_000,
    [property: Orleans.Id(7)] int MaxEdges = 5_000,
    [property: Orleans.Id(8)] ImmutableArray<string>? Labels = null);
