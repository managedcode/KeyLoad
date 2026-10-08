namespace KeyLoad;

/// <summary>Selects one explicitly bounded, stale committed document cut on a specified follower.</summary>
/// <param name="Version">The supported public format version, one.</param>
/// <param name="Reference">Exact document and atomic partition identity.</param>
/// <param name="ReplicaId">The actual native voter identity which must execute this read as a follower.</param>
/// <param name="MaximumLagPositions">Maximum difference from the fresh authorization cut, including control positions.</param>
/// <param name="MinimumToken">Optional acknowledged minimum required of the selected data cut.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(FollowerDocumentAliases.Request)]
public sealed record ReadFollowerDocumentRequestV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] EntityRef Reference,
    [property: Orleans.Id(2)] string ReplicaId,
    [property: Orleans.Id(3)] long MaximumLagPositions,
    [property: Orleans.Id(4)] CommitToken? MinimumToken = null);
